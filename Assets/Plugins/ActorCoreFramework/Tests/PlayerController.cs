using UnityEngine;
using UnityEngine.InputSystem;

namespace ActorCoreFramework.Tests
{
    public sealed class PlayerController : Controller2D
    {
        readonly InputAction moveAction;

        public PlayerController(Pawn2D pawn, InputAction moveAction) : base(pawn)
        {
            this.moveAction = moveAction;
            this.moveAction.started += OnMove;
            this.moveAction.performed += OnMove;
            this.moveAction.canceled += OnMove;
            this.moveAction.Enable();
        }

        protected override void OnDispose()
        {
            moveAction.Disable();
            moveAction.started -= OnMove;
            moveAction.performed -= OnMove;
            moveAction.canceled -= OnMove;
        }

        void OnMove(InputAction.CallbackContext ctx)
        {
            if (TryGetControlledPawn(out var pawn))
            {
                pawn.Move(ctx.ReadValue<Vector2>());
            }
        }
    }
}