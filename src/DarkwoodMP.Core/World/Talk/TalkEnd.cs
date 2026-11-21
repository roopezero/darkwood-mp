namespace DarkwoodMP.World.Talk
{
    /// What happens when our player's dialogue closes. Each but None first
    /// forgets the dialogue kept as ours.
    public enum TalkEnd
    {
        /// Nothing kept as ours: nothing.
        None,

        /// Forget it, and nothing more (no session, or no valid ref).
        Forget,

        /// Forget it and send the dialogue's changes (FlagsSync.FlushNow),
        /// no more: a client that isn't a client's copy any more.
        Flush,

        /// Forget it, send its changes, and as the host let go of the
        /// lock here.
        Release,

        /// Forget it, send its changes, and send the host the release
        /// (TalkLock with talking 0) after them.
        Send,
    }
}
