using UnityEngine;

namespace ActorCoreFramework
{
    public abstract class Pawn : Actor
    {
        public Transform Transform { get; }
        public virtual Vector3 Position => Transform.position;

        /// <summary>
        /// このPawnを操作しているController。未Possessならnull。
        /// UEのAPawn::GetController()相当。
        /// </summary>
        public Controller? Controller { get; internal set; }

        public bool IsControlled => Controller != null;

        protected Pawn(Transform transform)
        {
            Transform = transform;
        }

        /// <summary>
        /// Controllerから移動の「意図」を受け取る。
        /// 実際の移動処理はPawn側(MovementComponent等)の責務であり、
        /// Controllerは移動ロジックを知らない。UEのAPawn::AddMovementInput()相当。
        /// </summary>
        /// <param name="worldDirection">ワールド空間での移動方向。</param>
        /// <param name="scaleValue">方向に掛ける倍率。</param>
        public virtual void AddMovementInput(Vector3 worldDirection, float scaleValue = 1.0f) { }

        /// <summary>Possessされた直後に呼ばれる。この時点でControllerは設定済み。</summary>
        public virtual void OnPossessed() { }

        /// <summary>Unpossessされた直後に呼ばれる。この時点でControllerはnull。</summary>
        public virtual void OnUnpossessed() { }

        internal override void OnInternalEndPlay(EndPlayReason reason)
        {
            // 破棄されるPawnへの参照をControllerに残さない
            Controller?.ForceUnpossess();
        }
    }
}
