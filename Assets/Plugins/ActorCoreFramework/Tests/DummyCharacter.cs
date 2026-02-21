using UnityEngine;

namespace ActorCoreFramework.Tests
{
    public sealed class DummyCharacter : Character2D
    {
        public DummyCharacter(Transform transform, Rigidbody2D rigidbody, Collider2D collider, ContactFilter2D contactFilter) : base(transform, rigidbody, collider, contactFilter)
        {
        }

        protected override void OnDispose()
        {
        }
    }
}