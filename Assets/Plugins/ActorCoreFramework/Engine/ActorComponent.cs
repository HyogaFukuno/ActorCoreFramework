using System;

namespace ActorCoreFramework
{
    /// <summary>
    /// Actorに合成される機能単位。UnityのComponent(MonoBehaviour)とは無関係で、
    /// GameObjectの寿命から独立している。
    ///
    /// 寿命は所有者のActorに従う。生成はAddComponent、破棄はRemoveComponentか
    /// Actorの破棄に伴って行われ、外部からの単独破棄はできない。
    ///
    /// Tickは所有者のActorから配送されるため、TickGroupやIntervalは
    /// 所有者のPrimaryActorTickの設定に従う。Component単位では指定できない。
    /// </summary>
    public abstract class ActorComponent
    {
        bool attached;

        /// <summary>所有者。AddComponentされた時点で設定される。</summary>
        public Actor Owner { get; private set; } = null!;

        /// <summary>
        /// OnBeginPlayを受け取ってから、対になるOnEndPlayを受け取るまでの間true。
        /// 所有者のStateではなくこのフラグで判定することで、
        /// 所有者のEndPlay中に取り外された場合でも配送が対称に保たれる。
        /// </summary>
        public bool HasBegunPlay { get; private set; }

        /// <summary>実行時の一時停止用フラグ。falseの間はOnTickが呼ばれない。</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// RemoveComponentで取り外しが予約されている間true。
        /// Tick中に予約した場合、実際の除去はそのTickの完了後まで遅延する。
        /// </summary>
        public bool IsPendingRemoval { get; internal set; }

        public bool IsDisposed { get; private set; }


        /// <summary>
        /// 所有者を確定させる。ActorComponentは1つのActorだけに属する。
        /// </summary>
        internal void Attach(Actor owner)
        {
            if (IsDisposed)
            {
                throw new InvalidOperationException(
                    $"{GetType().Name} has already been disposed and cannot be attached again.");
            }

            if (attached)
            {
                throw new InvalidOperationException(
                    $"{GetType().Name} is already attached to {Owner}.");
            }

            attached = true;
            Owner = owner;
        }

        internal void DispatchBeginPlay()
        {
            if (HasBegunPlay) { return; }

            HasBegunPlay = true;
            OnBeginPlay();
        }

        internal void DispatchEndPlay(EndPlayReason reason)
        {
            // BeginPlayを受け取っていないなら、対になるEndPlayも配送しない
            if (!HasBegunPlay) { return; }

            HasBegunPlay = false;
            OnEndPlay(reason);
        }

        internal void DispatchTick(float deltaTime)
        {
            if (!Enabled) { return; }
            OnTick(deltaTime);
        }


        protected virtual void OnBeginPlay() { }
        protected virtual void OnEndPlay(EndPlayReason reason) { }
        protected virtual void OnTick(float deltaTime) { }
        protected virtual void OnDispose() { }


        /// <summary>所有者のActorからのみ呼ばれる。</summary>
        internal void Dispose()
        {
            if (IsDisposed) { return; }

            IsDisposed = true;
            OnDispose();
        }
    }
}
