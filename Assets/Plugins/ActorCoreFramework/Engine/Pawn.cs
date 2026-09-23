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

        // Possess状態はController側が唯一の保持先なので、通知の呼び出し口は
        // フレームワーク内部に閉じる。外から直接呼べると、Controllerは掴んだままなのに
        // Pawnだけが「解除された」と信じる、といった状態のズレを作れてしまう。
        internal void DispatchPossessed() => OnPossessed();
        internal void DispatchUnpossessed() => OnUnpossessed();

        /// <summary>Possessされた直後に呼ばれる。この時点でControllerは設定済み。</summary>
        protected virtual void OnPossessed() { }

        /// <summary>
        /// Unpossessされた直後に呼ばれる。この時点でControllerはnull。
        /// Pawn自身の破棄に伴う場合は、OnEndPlayより前に呼ばれる。
        /// この時点ではComponentもまだEndPlayを受け取っておらず、普段どおりに使える。
        /// </summary>
        protected virtual void OnUnpossessed() { }

        internal override void OnInternalBeginPlay()
        {
            // World未登録のうちにPossessされ、その後Controllerと別のWorldへ登録された。
            // Worldをまたいだ参照は残さない(Controller.CanPossessと同じ理由)。
            var controller = Controller;
            if (controller != null && !ReferenceEquals(controller.World, World))
            {
                controller.ForceUnpossess();
            }
        }

        internal override void OnInternalPreEndPlay()
        {
            // 破棄されるPawnへの参照をControllerに残さない。
            // OnEndPlayより後に回すと、後片付けを終えたPawnへOnUnpossessedが届き、
            // 既にEndPlayを受け取ったComponentを触ることになる。
            // UEでもPawnの破棄に伴うUnPossessはEndPlayより前に行われる。
            Controller?.ForceUnpossess();
        }
    }
}
