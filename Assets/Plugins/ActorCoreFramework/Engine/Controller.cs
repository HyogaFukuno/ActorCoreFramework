using System.Diagnostics.CodeAnalysis;

namespace ActorCoreFramework
{
    public abstract class Controller : Actor
    {
        Pawn? controlled;

        protected Controller(Pawn? controlled)
        {
            this.controlled = controlled;
            this.controlled?.OnPossessed();
        }

        public virtual void SwitchControlledPawn(Pawn? pawn)
        {
            controlled?.OnUnpossessed();
            controlled = pawn;
            controlled?.OnPossessed();
        }

        protected bool TryGetControlledPawn([NotNullWhen(true)] out Pawn? pawn)
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