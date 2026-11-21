namespace DarkwoodMP.Net.Steam
{
    /// What to do after Steam's answer to a queued message.
    public enum FlushStep : byte
    {
        /// It went: take it off the queue and try the next.
        Next,

        /// Still full: leave this peer's queue until next frame.
        Stop,

        /// Log "Send to {to} failed: {result}", take it off the queue
        /// anyway, and try the next.
        Drop,
    }
}
