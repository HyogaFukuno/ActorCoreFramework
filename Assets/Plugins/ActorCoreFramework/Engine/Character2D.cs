using System.Runtime.CompilerServices;
using UnityEngine;

namespace ActorCoreFramework
{
    public abstract class Character2D : Pawn2D
    {
        public Rigidbody2D Rigidbody { get; }
        public Collider2D Collider { get; }
        public ContactFilter2D ContactFilter { get; }
        public sealed override Vector2 Position => Rigidbody.position;

        protected Character2D(Transform transform, Rigidbody2D rigidbody, Collider2D collider, ContactFilter2D contactFilter) : base(transform)
        {
            Rigidbody = rigidbody;
            Collider = collider;
            ContactFilter = contactFilter;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsGrounded() => Rigidbody.IsTouching(ContactFilter);
    }
}