using UnityEngine;
using UnityEngine.InputSystem;

namespace ActorCoreFramework.Tests
{
    public class Startup : MonoBehaviour
    {
        [SerializeField] Rigidbody2D rigidbody = null!;
        [SerializeField] Collider2D collider = null!;
        [SerializeField] ContactFilter2D contactFilter;
        [SerializeField] InputAction moveAction = null!;
        DummyCharacter? dummy;
        PlayerController? controller;

        void OnEnable()
        {
            dummy = new DummyCharacter(transform, rigidbody, collider, contactFilter);
            controller = new PlayerController(dummy, moveAction);
        }

        void OnDisable()
        {
            controller?.Dispose();
            dummy?.Dispose();
        }
    }
}