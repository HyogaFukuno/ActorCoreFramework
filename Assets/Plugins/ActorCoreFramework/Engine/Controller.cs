using System;
using System.Diagnostics.CodeAnalysis;

namespace ActorCoreFramework
{
    /// <summary>
    /// Pawnを操作するActorの非ジェネリックな基底。
    /// PawnからControllerを型を問わず参照するために存在する。
    /// 実装はController&lt;TPawn&gt;を継承すること。
    /// </summary>
    public abstract class Controller : Actor
    {
        /// <summary>
        /// 操作中のPawn。型付きで取得するならTryGetControlledPawnを使う。
        /// Possess状態の唯一の保持先であり、Pawn.Controllerと常に対で更新される。
        /// </summary>
        public Pawn? ControlledPawn { get; private protected set; }

        public bool HasControlledPawn => ControlledPawn != null;

        /// <summary>
        /// Possessを強制解除する。Pawnの破棄時や、
        /// 他のControllerがPawnを奪う際にフレームワークから呼ばれる。
        /// </summary>
        internal abstract void ForceUnpossess();
    }

    public abstract class Controller<TPawn> : Controller where TPawn : Pawn
    {
        /// <summary>
        /// BeginPlay前に指定された操作対象。BeginPlayでPossessへ引き渡した後は使わない。
        /// Possess中の状態は基底のControlledPawnだけが持つ。
        /// </summary>
        TPawn? pendingPossess;

        bool possessing;

        protected Controller(TPawn? controlled)
        {
            // ここでは保持のみ。Possessは派生クラスの初期化完了後(BeginPlay)に行う。
            pendingPossess = controlled;
        }

        internal override void OnInternalBeginPlay()
        {
            var pawn = pendingPossess;
            pendingPossess = null; // Possessを一本の経路に通すため一度クリアする
            Possess(pawn);
        }

        internal override void OnInternalEndPlay(EndPlayReason reason) => Possess(null);

        internal override void ForceUnpossess() => Possess(null);

        /// <summary>
        /// 操作対象のPawnを差し替える。
        /// Possess状態の整合を保つため非virtual。差し替えに反応したい場合は
        /// Pawn側のOnPossessed / OnUnpossessedを使うこと。
        ///
        /// 破棄済み、または破棄が予約されたPawnを渡した場合はnullを渡したものとして扱い、
        /// 現在のPawnの解除だけが行われる。BeginPlay前に渡したPawnがその後破棄された場合も同様。
        /// </summary>
        public void SwitchControlledPawn(TPawn? pawn)
        {
            switch (State)
            {
                // BeginPlay前は参照の保持のみ。Possess通知はBeginPlayが行う。
                case ActorState.Created:
                    pendingPossess = pawn;
                    return;

                case ActorState.Playing:
                    Possess(pawn);
                    return;

                // EndPlay後の差し替えは受け付けない。
                default:
                    return;
            }
        }

        /// <summary>
        /// Possessの対象にできるかどうか。
        ///
        /// Pawnは自身のEndPlayでControllerの参照を切る(Pawn.OnInternalEndPlay)。
        /// EndPlayが済んだ後にPossessすると、その切断はもう行われないため、
        /// 破棄済みPawnへの参照がControllerに残り続ける。
        /// 破棄が予約されたPawnも、以降Tickが配送されないので検索系と同様に対象外とする。
        ///
        /// World未登録(Created)のPawnは許す。まだ死んでいないだけで、
        /// 後から登録されればBeginPlayが届き、破棄されればEndPlayで参照も切られる。
        /// </summary>
        static bool CanPossess(Pawn pawn) =>
            pawn.State is ActorState.Created or ActorState.Playing && !pawn.IsPendingDestroy;

        void Possess(TPawn? pawn)
        {
            // 掴めない相手は「指定されなかった」ものとして扱う。
            // 現在のPawnはこの後の経路で通常どおり解除される。
            if (pawn != null && !CanPossess(pawn)) { pawn = null; }

            if (ReferenceEquals(ControlledPawn, pawn)) { return; }

            // OnUnpossessed / OnPossessedの中から同じControllerのPossessが呼ばれると、
            // 中途半端な状態の上に別の差し替えが乗ってしまう。実装の誤りなので明示的に弾く。
            if (possessing)
            {
                throw new InvalidOperationException(
                    $"{GetType().Name} is already changing its controlled pawn. " +
                    "Do not call SwitchControlledPawn from OnPossessed or OnUnpossessed.");
            }

            possessing = true;

            try
            {
                if (ControlledPawn != null)
                {
                    var previous = ControlledPawn;
                    ControlledPawn = null;

                    previous.Controller = null;
                    previous.DispatchUnpossessed();
                }

                if (pawn == null) { return; }

                // 既に他のControllerが操作しているなら、先にそちらを解除する
                pawn.Controller?.ForceUnpossess();

                ControlledPawn = pawn;

                pawn.Controller = this;
                pawn.DispatchPossessed();
            }
            finally
            {
                possessing = false;
            }
        }

        protected bool TryGetControlledPawn([NotNullWhen(true)] out TPawn? pawn)
        {
            pawn = ControlledPawn as TPawn;
            return pawn != null;
        }
    }
}
