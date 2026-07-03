using UnityEngine;

namespace ActorCoreFramework
{
    public abstract class Character : Pawn
    {
        public Rigidbody Rigidbody { get; }
        public Collider Collider { get; }
        public sealed override Vector3 Position => Rigidbody.position;

        protected Character(Transform transform, Rigidbody rigidbody, Collider collider) : base(transform)
        {
            Rigidbody = rigidbody;
            Collider = collider;
        }
    }
}