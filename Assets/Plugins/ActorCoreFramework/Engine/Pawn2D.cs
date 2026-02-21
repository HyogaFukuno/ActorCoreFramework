using UnityEngine;

namespace ActorCoreFramework
{
    public abstract class Pawn2D : Actor
    {
        readonly Transform transform;
        
        public virtual Vector2 Position => transform.position;

        protected Pawn2D(Transform transform)
        {
            this.transform = transform;
        }
        
        public virtual void OnPossessed() { }
        public virtual void OnUnpossessed() { }

        public virtual void SetPosition(Vector2 position) => transform.position = new Vector3(position.x, position.y);
        public virtual void Move(Vector2 velocity) => transform.position += new Vector3(velocity.x, velocity.y);
        public virtual void MoveX(float x) => transform.position += new Vector3(x, 0);
        public virtual void MoveY(float y) => transform.position += new Vector3(0, y);
    }
}