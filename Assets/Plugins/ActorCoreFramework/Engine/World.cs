using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ActorCoreFramework
{
    /// <summary>
    /// Actorの所有権、生成、破棄、Tickを扱うクラス。
    ///
    /// スレッドセーフではない。生成したスレッド(通常はUnityのメインスレッド)を所有スレッドとし、
    /// すべての操作はそこから行うこと。例外はActor.BindToに渡したトークンのキャンセルで、
    /// 別スレッドから発火しても所有スレッドへ受け渡してから処理する。
    /// </summary>
    public sealed class World : IDisposable
    {
        /// <summary>
        /// Idの発番元。Actorと同じく、Domain Reloadを無効にしても意図的にリセットしない。
        /// </summary>
        static int s_nextId;

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

        /// <summary>
        /// 所有スレッド以外から届いた破棄要求。所有スレッドのTickの冒頭で取り出して処理する。
        /// </summary>
        readonly ConcurrentQueue<Actor> crossThreadDestroyRequests = new();

        readonly int ownerThreadId = Environment.CurrentManagedThreadId;

        // 所有スレッド以外から読まれるのでvolatileにする
        volatile bool disposed;
        bool ticking;

        string? name;

        /// <summary>
        /// プロセス内で一意な識別子。生成順に1から発番される。
        /// </summary>
        public int Id { get; } = System.Threading.Interlocked.Increment(ref s_nextId);

        /// <summary>
        /// デバッグ表示用の名前。既定は「World#Id」。
        /// 複数のWorldを使う場合、World Debuggerで見分けられるよう名前を付けておくとよい。
        /// nullや空文字を代入すると既定へ戻る。
        /// </summary>
        public string Name
        {
            get => name ??= $"{nameof(World)}#{Id}";
            set => name = string.IsNullOrEmpty(value) ? null : value;
        }

        /// <summary>破棄済みならtrue。</summary>
        public bool IsDisposed => disposed;

        /// <summary>
        /// 登録されているActor。破棄が予約されたActorや、PostTick末尾の破棄処理の最中にある
        /// Actorも含む(破棄処理が終わった時点でまとめて取り除かれる)。
        /// 生きているActorだけを扱いたい場合はGetActorsを使うこと。
        /// </summary>
        public IReadOnlyList<Actor> Actors => actors;


        public World()
        {
            // Editor上のデバッグ表示のためだけに控える。ビルドでは何もしない
            WorldRegistry.Add(this);
        }


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
        /// BeginPlayが例外を投げた場合、登録は巻き戻されてActorは破棄され、例外はそのまま伝わる。
        /// </summary>
        /// <exception cref="ArgumentNullException">actorがnull。</exception>
        /// <exception cref="InvalidOperationException">actorが既に登録済みか破棄済み。</exception>
        /// <exception cref="ObjectDisposedException">このWorldが破棄済み。</exception>
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

                // 末尾付近にいるはずなので後ろから探す。BeginPlay中に更に登録された分だけ前にずれる
                actors.RemoveAt(actors.LastIndexOf(actor));

                // 巻き戻しのEndPlayは、初期化が途中で止まったActorに届く。
                // OnBeginPlayで確保するはずだったものを解放しようとして、ここでも例外になりやすい。
                // それをそのまま伝えると本来の原因であるBeginPlayの例外が消えるので、
                // 巻き戻しの例外はログに出すだけにして、呼び出し元へはBeginPlayの例外を投げ直す。
                try { actor.Dispose(); }
                catch (Exception rollbackException) { UnityEngine.Debug.LogException(rollbackException); }

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

        /// <summary>
        /// Actor.BindToで紐づけた寿命が尽きたときの破棄要求。
        /// トークンはどのスレッドからでもキャンセルされうるので、所有スレッド以外からの要求は
        /// キューへ積むだけにして、次に所有スレッドでTickが回った冒頭で受け付ける。
        /// 所有スレッドからの要求は、これまでどおりその場でDestroyへ流す。
        /// </summary>
        internal void DestroyFromLifetime(Actor actor)
        {
            if (Environment.CurrentManagedThreadId == ownerThreadId)
            {
                Destroy(actor);
                return;
            }

            if (disposed) { return; }

            crossThreadDestroyRequests.Enqueue(actor);
        }

        void DrainCrossThreadDestroyRequests()
        {
            // Destroyは状態を確かめて黙って無視するので、その間に死んだActorが混ざっていてもよい
            while (crossThreadDestroyRequests.TryDequeue(out var actor))
            {
                Destroy(actor);
            }
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

            var failures = new FailureCollector();

            try
            {
                DrainCrossThreadDestroyRequests();

                // Tickが例外で抜けても破棄予約は必ず処理する。
                // ここを飛ばすと、Destroyされたはずの Actor が次フレームまで生き残る。
                try { tickGroups[(int)TickGroup.PostTick].Tick(deltaTime); }
                catch (Exception e) { failures.Add(e); }

                // 破棄もTickの一部とみなし、この間の再入を許さない
                try { FlushPendingDestroy(); }
                catch (Exception e) { failures.Add(e); }
            }
            finally
            {
                ticking = false;
            }

            failures.ThrowIfAny();
        }

        void TickGroupCore(TickGroup group, float deltaTime)
        {
            if (disposed) { return; }

            ThrowIfTicking();
            ticking = true;

            try
            {
                // 別スレッドで寿命が尽きたActorも、Tickを配送する前に破棄予約へ回す
                DrainCrossThreadDestroyRequests();
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
            // 最初の例外は予約をすべて処理し終えてから呼び出し元へ投げ直す。
            var failures = new FailureCollector();

            try
            {
                // 破棄処理中にさらにDestroyが積まれても同一フレームで拾う
                for (var i = 0; i < pendingDestroy.Count; i++)
                {
                    var actor = pendingDestroy[i];

                    UnregisterTick(actor);

                    try { actor.DispatchEndPlay(EndPlayReason.Destroyed); }
                    catch (Exception e) { failures.Add(e); }

                    try { actor.Dispose(); }
                    catch (Exception e) { failures.Add(e); }
                }
            }
            finally
            {
                pendingDestroy.Clear();
                pendingDestroySet.Clear();

                // 1体ずつList.Removeすると、大量に破棄したフレームがO(n^2)になる。
                // 破棄し終えたActorは最後にまとめて取り除く。
                RemoveDisposedActors();
                foreach (var group in tickGroups) { group.Compact(); }
            }

            failures.ThrowIfAny();
        }

        /// <summary>
        /// 破棄し終えたActorを、並びを保ったまま一度の走査で取り除く。
        /// Worldに登録されたActorがDisposedになる経路は破棄処理だけなので、状態で判別できる。
        /// </summary>
        void RemoveDisposedActors()
        {
            var count = 0;
            for (var i = 0; i < actors.Count; i++)
            {
                var actor = actors[i];
                if (actor.State == ActorState.Disposed) { continue; }

                actors[count++] = actor;
            }

            actors.RemoveRange(count, actors.Count - count);
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
            var failures = new FailureCollector();

            // 生成と逆順に破棄する(依存の切断順序を安定させる)
            for (var i = actors.Count - 1; i >= 0; i--)
            {
                var actor = actors[i];
                actor.PrimaryActorTick.registered = false;

                try { actor.DispatchEndPlay(EndPlayReason.WorldShutdown); }
                catch (Exception e) { failures.Add(e); }

                try { actor.Dispose(); }
                catch (Exception e) { failures.Add(e); }
            }

            actors.Clear();
            pendingDestroy.Clear();
            pendingDestroySet.Clear();
            foreach (var group in tickGroups) { group.Clear(); }

            // 積まれていた破棄要求は、もう受け付ける先がないので捨てる。
            // disposedの確認と積み込みの間に割り込まれて1件残っても、Worldごと解放されるだけで害はない。
            while (crossThreadDestroyRequests.TryDequeue(out _)) { }

            WorldRegistry.Remove(this);

            failures.ThrowIfAny();
        }

        public override string ToString() => Name;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ThrowIfDisposed()
        {
            if (disposed) { throw new ObjectDisposedException(nameof(World)); }
        }
    }
}
