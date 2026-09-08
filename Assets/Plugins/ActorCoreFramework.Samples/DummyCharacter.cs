using UnityEngine;

namespace ActorCoreFramework.Samples
{
    public sealed class DummyCharacter : Character
    {
        readonly MovementComponent movement;


        public DummyCharacter(Transform transform, Rigidbody rigidbody, Collider collider) : base(transform, rigidbody, collider)
        {
            // Rigidbodyへ速度を書き込むのでFixedTick
            PrimaryActorTick.CanEverTick = true;
            PrimaryActorTick.Group = TickGroup.FixedTick;

            movement = AddComponent(new MovementComponent(rigidbody, new MovementSettings(5.0f)));
        }

        public override void AddMovementInput(Vector3 worldDirection, float scaleValue = 1.0f)
        {
            movement.AddInput((Vector2)worldDirection * scaleValue);
        }

        protected override void OnDispose()
        {
            Debug.Log("DummyCharacter.Dispose");
        }
    }
}
