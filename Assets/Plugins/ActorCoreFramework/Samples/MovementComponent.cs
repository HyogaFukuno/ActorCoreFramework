using UnityEngine;

namespace ActorCoreFramework.Samples
{
    public readonly struct MovementSettings
    {
        public readonly float MaxSpeed;

        public MovementSettings(float maxSpeed)
        {
            MaxSpeed = maxSpeed;
        }
    }

    /// <summary>
    /// 蓄積された入力を水平方向の速度へ変換する。Rigidbodyへ書き込むため、
    /// 所有者のPrimaryActorTickはFixedTickに設定しておくこと。
    /// また、入力を積むController側は同じグループでより小さいPriorityを設定し、
    /// このComponentより先にTickされるようにすること。
    /// </summary>
    public sealed class MovementComponent : ActorComponent
    {
        readonly Rigidbody rigidbody;
        readonly MovementSettings settings;
        Vector2 inputDirection;

        public MovementComponent(Rigidbody rigidbody, in MovementSettings settings)
        {
            this.rigidbody = rigidbody;
            this.settings = settings;
        }

        /// <summary>
        /// 移動入力を加算する。Tickで消費されるまで蓄積される。
        /// UEのAPawn::AddMovementInput()とConsumeMovementInputVector()の関係に倣う。
        /// </summary>
        public void AddInput(Vector2 direction) => inputDirection += direction;

        protected override void OnTick(float deltaTime)
        {
            // 複数の入力源から積まれうるので、方向の大きさは1に丸める
            var target = Vector2.ClampMagnitude(inputDirection, 1.0f) * settings.MaxSpeed;
            inputDirection = Vector2.zero;

            // 入力は水平面(xz)へ割り当てる。
            // 鉛直方向は重力やジャンプの領分なので上書きしない。
            var velocity = rigidbody.linearVelocity;
            rigidbody.linearVelocity = new Vector3(target.x, velocity.y, target.y);
        }
    }
}
