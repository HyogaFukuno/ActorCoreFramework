using System.Runtime.CompilerServices;
using UnityEngine;

namespace ActorCoreFramework
{
    public abstract class Character2D : Pawn2D
    {
        protected readonly Rigidbody2D rigidbody;
        protected readonly Collider2D collider;
        protected readonly ContactFilter2D contactFilter;

        public sealed override Vector2 Position => rigidbody.position;

        protected Character2D(Transform transform, Rigidbody2D rigidbody, Collider2D collider, ContactFilter2D contactFilter) : base(transform)
        {
            this.rigidbody = rigidbody;
            this.collider = collider;
            this.contactFilter = contactFilter;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsGrounded() => rigidbody.IsTouching(contactFilter);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public sealed override void Move(Vector2 velocity) => rigidbody.linearVelocity = velocity;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public sealed override void MoveX(float speed) => rigidbody.linearVelocityX = speed;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public sealed override void MoveY(float speed) => rigidbody.linearVelocityY = speed;
    }
}