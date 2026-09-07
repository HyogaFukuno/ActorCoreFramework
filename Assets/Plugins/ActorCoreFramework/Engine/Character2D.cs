using UnityEngine;

namespace ActorCoreFramework
{
    public abstract class Character2D : Pawn2D
    {
        public Rigidbody2D Rigidbody { get; }
        public Collider2D Collider { get; }

        /// <summary>
        /// 物理の補間が効いた座標を返す。zはPawn2D側でTransformから補われる。
        /// </summary>
        public sealed override Vector2 Position2D => Rigidbody.position;

        protected Character2D(Transform transform, Rigidbody2D rigidbody, Collider2D collider) : base(transform)
        {
            Rigidbody = rigidbody;
            Collider = collider;
        }
    }
}
