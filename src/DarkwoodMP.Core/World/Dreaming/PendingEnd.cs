namespace DarkwoodMP.World.Dreaming
{
    /// What a client does with a pending end each frame.
    public enum PendingEnd
    {
        /// Kept: our dream hasn't begun.
        Keep,

        /// Cleared: our dream had ended already.
        Drop,

        /// Cleared, and our dream ends.
        End,
    }
}
