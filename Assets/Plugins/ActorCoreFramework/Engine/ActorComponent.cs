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
        /// <summary>所有者。AddComponentされた時点で設定される。</summary>
        public Actor Owner { get; private set; } = null!;

        /// <summary>実行時の一時停止用フラグ。falseの間はOnTickが呼ばれない。</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// RemoveComponentで取り外しが予約されている間true。
        /// Tick中に予約した場合、実際の除去はそのTickの完了後まで遅延する。
        /// </summary>
        public bool IsPendingRemoval { get; internal set; }

        public bool IsDisposed { get; private set; }


        internal void Attach(Actor owner) => Owner = owner;

        internal void DispatchBeginPlay() => OnBeginPlay();

        internal void DispatchEndPlay(EndPlayReason reason) => OnEndPlay(reason);

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
