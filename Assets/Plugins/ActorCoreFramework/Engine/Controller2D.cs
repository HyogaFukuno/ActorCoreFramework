using System.Diagnostics.CodeAnalysis;

namespace ActorCoreFramework
{
    public abstract class Controller2D : Actor
    {
        Pawn2D? controlled;

        protected Controller2D(Pawn2D? controlled)
        {
            this.controlled = controlled;
            this.controlled?.OnPossessed();
        }

        public virtual void SwitchControlledPawn(Pawn2D? pawn)
        {
            controlled?.OnUnpossessed();
            controlled = pawn;
            controlled?.OnPossessed();
        }

        protected bool TryGetControlledPawn([NotNullWhen(true)] out Pawn2D? pawn)
        {
            if (controlled != null)
            {
                pawn = controlled;
                return true;
            }

            pawn = null;
            return false;
        }
    }
}