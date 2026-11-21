namespace DarkwoodMP.World.Dreaming
{
    /// What a client does with the host's DreamEnd.
    public enum ClientEndStep
    {
        /// Dropped.
        Drop,

        /// Kept as the pending end, for when our dream has begun.
        KeepPending,

        /// Our dream ends.
        End,
    }
}
