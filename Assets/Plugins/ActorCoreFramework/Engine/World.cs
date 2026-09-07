using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

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

        // TickGroupの要素数に追従させる。列挙子を増やしても初期化漏れが起きない。
        readonly TickGroupList[] tickGroups = CreateTickGroups();

        bool disposed;

        public IReadOnlyList<Actor> Actors => actors;


        static TickGroupList[] CreateTickGroups()
        {
            var groups = new TickGroupList[Enum.GetValues(typeof(TickGroup)).Length];
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

            try
            {
                actor.DispatchBeginPlay(this); // ここでPossessなどが走る
            }
            catch
            {
                // BeginPlayが失敗したActorをPlayingのまま残さない
                actors.Remove(actor);
                actor.Dispose();
                throw;
            }

            var tick = actor.PrimaryActorTick;
            if (tick is { CanEverTick: true, registered: false })
            {
                tick.registered = true;
                tick.registeredGroup = tick.Group;
                tickGroups[(int)tick.registeredGroup].Add(actor);
            }

            return actor;
        }

        /// <summary>
        /// Worldからの破棄を予約する。実際の破棄はPostTickの末尾。
        /// 予約された時点でActor.IsPendingDestroyがtrueになり、以降Tickは配送されない。
        /// </summary>
        public void Destroy(Actor actor)
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

        public void PostTick(float deltaTime)
        {
            if (disposed) { return; }

            tickGroups[(int)TickGroup.PostTick].Tick(deltaTime);
            FlushPendingDestroy();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void TickGroupCore(TickGroup group, float deltaTime)
        {
            if (disposed) { return; }
            tickGroups[(int)group].Tick(deltaTime);
        }

        void FlushPendingDestroy()
        {
            if (pendingDestroy.Count <= 0) { return; }

            // 破棄処理中にさらにDestroyが積まれても同一フレームで拾う
            for (var i = 0; i < pendingDestroy.Count; i++)
            {
                var actor = pendingDestroy[i];
                Unregister(actor, EndPlayReason.Destroyed);
                actor.Dispose();
            }

            pendingDestroy.Clear();
            pendingDestroySet.Clear();
        }

        void Unregister(Actor actor, EndPlayReason reason)
        {
            var tick = actor.PrimaryActorTick;
            if (tick.registered)
            {
                tick.registered = false;
                // Groupが実行時に変更されていても、登録先から確実に外す
                tickGroups[(int)tick.registeredGroup].Remove(actor);
            }

            actors.Remove(actor);
            actor.DispatchEndPlay(reason);
        }

        public void Dispose()
        {
            if (disposed) { return; }
            disposed = true;

            // 生成と逆順に破棄する(依存の切断順序を安定させる)
            for (var i = actors.Count - 1; i >= 0; i--)
            {
                var actor = actors[i];
                actor.PrimaryActorTick.registered = false;
                actor.DispatchEndPlay(EndPlayReason.WorldShutdown);
                actor.Dispose();
            }

            actors.Clear();
            pendingDestroy.Clear();
            pendingDestroySet.Clear();
            foreach (var group in tickGroups) { group.Clear(); }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ThrowIfDisposed()
        {
            if (disposed) { throw new ObjectDisposedException(nameof(World)); }
        }
    }
}
