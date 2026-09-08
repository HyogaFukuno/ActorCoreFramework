using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ActorCoreFramework.Samples
{
    public class Startup : MonoBehaviour
    {
        [SerializeField] Rigidbody rigidbody = null!;
        [SerializeField] Collider collider = null!;
        [SerializeField] InputAction moveAction = null!;

        World? world;
        IDisposable? loop;

        void Awake()
        {
            world = new World();

            var pawn = world.Spawn(() => new DummyCharacter(transform, rigidbody, collider));
            world.Spawn(() => new PlayerController(pawn, moveAction));
            
            loop = WorldLoop.Register(world);
        }

        void OnDestroy()
        {
            loop?.Dispose();
            world?.Dispose();
        }
    }
}