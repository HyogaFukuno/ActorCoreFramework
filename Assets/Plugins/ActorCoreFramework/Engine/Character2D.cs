using UnityEngine;

namespace ActorCoreFramework
{
    public abstract class Character2D : Pawn2D
    {
        public Rigidbody2D Rigidbody { get; }
        public Collider2D Collider { get; }

        /// <summary>
        /// 物理ステップ上の座標を返す。zはPawn2D側でTransformから補われる。
        ///
        /// Rigidbody2Dの補間(Interpolate)はTransformにだけ反映され、この値には効かない。
        /// FixedUpdateの間隔でしか変わらないので、カメラ追従や描画位置の基準には
        /// 補間済みのTransform.positionを使うこと。
        /// </summary>
        public sealed override Vector2 Position2D => Rigidbody.position;

        protected Character2D(Transform transform, Rigidbody2D rigidbody, Collider2D collider) : base(transform)
        {
            Rigidbody = rigidbody;
            Collider = collider;
        }
    }
}
