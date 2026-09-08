using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace ActorCoreFramework
{
    /// <summary>
    /// Actorの所有権、生成、破棄、Tickを扱うクラス。
    /// </summary>
    public sealed class World : IDisposable
    {
        readonly List<Actor> actors = new();

        // 予約順を保つListと、重複判定用のSetを併用する
        readonly List<Actor> pendingDestroy = new();
        readonly HashSet<Actor> pendingDestroySet = new();

        /// <summary>
        /// TickGroupの値をそのまま添字に使うため、最大値+1を長さとする。
        /// 列挙子を増やしても、値が連番でなくても初期化漏れや添字外れが起きない。
        /// Worldごとにリフレクションを走らせないよう、一度だけ求める。
        /// </summary>
        static readonly int s_tickGroupCount = GetTickGroupCount();

        readonly TickGroupList[] tickGroups = CreateTickGroups();

        bool disposed;
        bool ticking;

        public IReadOnlyList<Actor> Actors => actors;


        static int GetTickGroupCount()
        {
            var max = 0;
            foreach (TickGroup group in Enum.GetValues(typeof(TickGroup)))
            {
                if ((int)group > max) { max = (int)group; }
            }

            return max + 1;
        }

        static TickGroupList[] CreateTickGroups()
        {
            var groups = new TickGroupList[s_tickGroupCount];
            for (var i = 0; i < groups.Length; i++) { groups[i] = new TickGroupList(); }

            return groups;
        }


        /// <summary>
        /// Actorを生成してWorldに登録させる。
        /// factory内でコンストラクタ依存を解決させる。
        /// </summary>
        public T Spawn<T>(Func<T> factory) where T : Actor
        {
            ThrowIfDisposed();

            if (factory == null) { throw new ArgumentNullException(nameof(factory)); }

            var actor = factory() ?? throw new InvalidOperationException($"The factory returned null for {typeof(T).Name}.");
            return Register(actor);
        }


        /// <summary>
        /// 既に生成されたActorをWorldに登録させる。
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public T Register<T>(T actor) where T : Actor
        {
            ThrowIfDisposed();

            if (actor == null) { throw new ArgumentNullException(nameof(actor)); }

            if (actor.State != ActorState.Created)
            {
                throw new InvalidOperationException($"{typeof(T).Name} is already registered or disposed");
            }

            actors.Add(actor);

            // Tickグループへの登録はBeginPlayより先に済ませる。
            // 後に回すと、BeginPlayの中で更にRegisterされたActorが先にグループへ入り、
            // Priorityが同値のときの「登録順を保つ」という約束が破れる。
            var tick = actor.PrimaryActorTick;
            if (tick is { CanEverTick: true, registered: false })
            {
                tick.registered = true;
                tick.registeredGroup = tick.Group;
                tickGroups[(int)tick.registeredGroup].Add(actor);
            }

            // Tickグループへ登録されなかった場合も、以降の設定変更は受け付けない。
            // 黙って効かないより、その場で気付けるようにする。
            // BeginPlayより前にロックすることで、OnBeginPlayでの変更も同じ扱いになる。
            tick.locked = true;

            try
            {
                actor.DispatchBeginPlay(this); // ここでPossessなどが走る
            }
            catch
            {
                // BeginPlayが失敗したActorをPlayingのまま残さない。
                // 先に済ませたTickグループへの登録も巻き戻す。
                UnregisterTick(actor);
                actors.Remove(actor);
                actor.Dispose();
                throw;
            }

            return actor;
        }

        /// <summary>
        /// 指定した型のActorをすべて集める。UEのGetAllActorsOfClass相当。
        /// 呼び出し側のリストへ詰めるので、リストを使い回せば毎フレーム呼んでも
        /// アロケーションが発生しない。破棄が予約されたActorは対象外。
        /// </summary>
        /// <param name="results">結果の格納先。呼び出しごとにクリアされる。</param>
        /// <returns>集めた件数。</returns>
        public int GetActors<T>(List<T> results) where T : Actor
        {
            if (results == null) { throw new ArgumentNullException(nameof(results)); }

            results.Clear();

            for (var i = 0; i < actors.Count; i++)
            {
                if (actors[i] is T found && !found.IsPendingDestroy) { results.Add(found); }
            }

            return results.Count;
        }

        /// <summary>
        /// Worldからの破棄を予約する。実際の破棄はPostTickの末尾。
        /// 予約された時点でActor.IsPendingDestroyがtrueになり、以降Tickは配送されない。
        ///
        /// null、既に破棄済み、他のWorldが所有するActorは黙って無視する。
        /// 破棄は「その状態へ持っていく」要求なので、既にそうなっているなら成功と同じ扱いでよい。
        /// </summary>
        public void Destroy(Actor? actor)
        {
            if (disposed) { return; }
            if (actor == null) { return; }
            if (actor.State != ActorState.Playing) { return; }

            // 他のWorldが所有するActorは受け付けない
            if (!ReferenceEquals(actor.World, this)) { return; }

            if (!pendingDestroySet.Add(actor)) { return; }

            actor.IsPendingDestroy = true;
            pendingDestroy.Add(actor);
        }

        public void Tick(float deltaTime) => TickGroupCore(TickGroup.Tick, deltaTime);
        public void FixedTick(float deltaTime) => TickGroupCore(TickGroup.FixedTick, deltaTime);

        /// <summary>
        /// Time.timeScaleの影響を受けないdeltaTimeで回すグループ。
        /// 渡すdeltaTimeの種類は呼び出し側の責務で、World自身は区別しない。
        /// </summary>
        public void UnscaledTick(float deltaTime) => TickGroupCore(TickGroup.UnscaledTick, deltaTime);

        /// <summary>
        /// PostTickグループを回し、末尾で破棄予約を処理する。
        /// </summary>
        public void PostTick(float deltaTime)
        {
            if (disposed) { return; }

            ThrowIfTicking();
            ticking = true;

            ExceptionDispatchInfo? failure = null;

            try
            {
                // Tickが例外で抜けても破棄予約は必ず処理する。
                // ここを飛ばすと、Destroyされたはずの Actor が次フレームまで生き残る。
                try { tickGroups[(int)TickGroup.PostTick].Tick(deltaTime); }
                catch (Exception e) { failure = ExceptionDispatchInfo.Capture(e); }

                // 破棄もTickの一部とみなし、この間の再入を許さない
                FlushPendingDestroy();
            }
            finally
            {
                ticking = false;
            }

            failure?.Throw();
        }

        void TickGroupCore(TickGroup group, float deltaTime)
        {
            if (disposed) { return; }

            ThrowIfTicking();
            ticking = true;

            try
            {
                tickGroups[(int)group].Tick(deltaTime);
            }
            finally
            {
                ticking = false;
            }
        }

        /// <summary>
        /// Tickの最中にWorldを回そうとしていないか確かめる。
        ///
        /// 入れ子で回すと、内側の完了時にTickGroupListの反復中フラグが落ちる。
        /// 以降の増減が保留に回らず反復中のリストへ即時反映され、添字がずれて
        /// Tickの取りこぼしや二重配送になる。黙って壊れるので明示的に弾く。
        /// </summary>
        void ThrowIfTicking()
        {
            if (!ticking) { return; }

            throw new InvalidOperationException(
                $"{nameof(World)} is already ticking. Do not drive the world from within a tick.");
        }

        void FlushPendingDestroy()
        {
            if (pendingDestroy.Count <= 0) { return; }

            // 1体の失敗で残りのActorが後始末を受け取れなくなるのを防ぐ。
            // 最初の例外だけを控え、予約をすべて処理し終えてから呼び出し元へ投げ直す。
            ExceptionDispatchInfo? failure = null;

            // 破棄処理中にさらにDestroyが積まれても同一フレームで拾う
            for (var i = 0; i < pendingDestroy.Count; i++)
            {
                var actor = pendingDestroy[i];

                try { Unregister(actor, EndPlayReason.Destroyed); }
                catch (Exception e) { failure ??= ExceptionDispatchInfo.Capture(e); }

                try { actor.Dispose(); }
                catch (Exception e) { failure ??= ExceptionDispatchInfo.Capture(e); }
            }

            pendingDestroy.Clear();
            pendingDestroySet.Clear();

            failure?.Throw();
        }

        void Unregister(Actor actor, EndPlayReason reason)
        {
            UnregisterTick(actor);

            actors.Remove(actor);
            actor.DispatchEndPlay(reason);
        }

        void UnregisterTick(Actor actor)
        {
            var tick = actor.PrimaryActorTick;
            if (!tick.registered) { return; }

            tick.registered = false;
            // Groupが実行時に変更されていても、登録先から確実に外す
            tickGroups[(int)tick.registeredGroup].Remove(actor);
        }

        /// <summary>
        /// Worldを破棄し、所有するActorすべてにWorldShutdownのEndPlayを配送する。
        /// </summary>
        /// <exception cref="InvalidOperationException">Tickの最中に呼んだ。</exception>
        public void Dispose()
        {
            if (disposed) { return; }

            // Tickの最中に破棄すると、TickGroupListが反復中のリストをClearすることになり
            // そのフレームの残りのActorが黙って落ちる。Tickと同じ理由で明示的に弾く。
            ThrowIfTicking();

            disposed = true;

            // 1体の失敗で残りのActorが後始末を受け取れなくなるのを防ぐ。
            // ここで打ち切るとdisposedだけが立ち、二度目のDisposeも即returnするため
            // 残りのActorは永久にEndPlayもDisposeも受け取れなくなる。
            ExceptionDispatchInfo? failure = null;

            // 生成と逆順に破棄する(依存の切断順序を安定させる)
            for (var i = actors.Count - 1; i >= 0; i--)
            {
                var actor = actors[i];
                actor.PrimaryActorTick.registered = false;

                try { actor.DispatchEndPlay(EndPlayReason.WorldShutdown); }
                catch (Exception e) { failure ??= ExceptionDispatchInfo.Capture(e); }

                try { actor.Dispose(); }
                catch (Exception e) { failure ??= ExceptionDispatchInfo.Capture(e); }
            }

            actors.Clear();
            pendingDestroy.Clear();
            pendingDestroySet.Clear();
            foreach (var group in tickGroups) { group.Clear(); }

            failure?.Throw();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ThrowIfDisposed()
        {
            if (disposed) { throw new ObjectDisposedException(nameof(World)); }
        }
    }
}
