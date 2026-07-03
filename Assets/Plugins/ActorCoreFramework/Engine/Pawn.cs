using UnityEngine;

namespace ActorCoreFramework
{
    public abstract class Pawn : Actor
    {
        public Transform Transform { get; }
        public virtual Vector3 Position => Transform.position;

        protected Pawn(Transform transform)
        {
            Transform = transform;
        }
        
        public virtual void OnPossessed() { }
        public virtual void OnUnpossessed() { }
    }
}