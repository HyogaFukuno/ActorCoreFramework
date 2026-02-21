using System;

namespace ActorCoreFramework
{
    public abstract class Actor : IDisposable
    {
        bool disposed;
        
        protected abstract void OnDispose();
        
        public void Dispose()
        {
            if (disposed) { return; }

            disposed = true;
            OnDispose();
            GC.SuppressFinalize(this);
        }
    }
}