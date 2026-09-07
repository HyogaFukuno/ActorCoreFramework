namespace ActorCoreFramework
{
    public enum ActorState
    {
        Created,
        Playing,
        Ended,
        Disposed
    }

    public enum EndPlayReason
    {
        Destroyed,
        WorldShutdown
    }
}