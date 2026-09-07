using UnityEngine;
using UnityEngine.InputSystem;

namespace ActorCoreFramework.Samples
{
    public sealed class PlayerController : Controller<Character>
    {
        readonly InputAction moveAction;

        public PlayerController(Character pawn, InputAction moveAction) : base(pawn)
        {
            this.moveAction = moveAction;

            // 積んだ入力を同じ物理ステップ内で消費させるため、操作対象と同じFixedTickに置く。
            // Update側で積むとFixedUpdateが先に走る分だけ遅延し、
            // 1フレームに複数回FixedUpdateが走ると取りこぼす。
            PrimaryActorTick.CanEverTick = true;
            PrimaryActorTick.Group = TickGroup.FixedTick;

            // 操作対象のPawnより先にTickされ、入力を積んでから消費させる
            PrimaryActorTick.Priority = -100;
        }

        protected override void OnBeginPlay()
        {
            moveAction.Enable();
        }

        protected override void OnTick(float deltaTime)
        {
            if (TryGetControlledPawn(out var pawn))
            {
                // Controllerは「意図」を渡すだけ。移動ロジックはPawn側が持つ。
                pawn.AddMovementInput(moveAction.ReadValue<Vector2>());
            }
        }

        protected override void OnEndPlay(EndPlayReason reason)
        {
            moveAction.Disable();
        }

        protected override void OnDispose()
        {
            Debug.Log("PlayerController.Dispose");
        }
    }
}
