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

            try
            {
                var pawn = world.Spawn(() => new DummyCharacter(transform, rigidbody, collider));
                world.Spawn(() => new PlayerController(pawn, moveAction));

                loop = WorldLoop.Register(world);
            }
            catch
            {
                // 途中まで作ったWorldをここで畳む。
                // Awakeが例外で抜けたときにOnDestroyが呼ばれるかどうかに頼らない
                world.Dispose();
                world = null;
                throw;
            }
        }

        void OnDestroy()
        {
            loop?.Dispose();
            world?.Dispose();
        }
    }
}