using UnityEngine;

namespace ActorCoreFramework
{
    public abstract class Pawn2D : Actor
    {
        public Transform Transform { get; }
        public virtual Vector2 Position => Transform.position;

        protected Pawn2D(Transform transform)
        {
            Transform = transform;
        }
        
        public virtual void OnPossessed() { }
        public virtual void OnUnpossessed() { }
    }
}