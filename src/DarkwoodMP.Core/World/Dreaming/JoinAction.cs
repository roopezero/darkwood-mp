namespace DarkwoodMP.World.Dreaming
{
    /// What a client does with a pending start this frame.
    public enum JoinAction
    {
        /// Dropped: not in the host's world any more, or in a dream.
        Drop,

        /// Dropped with a warning: too long busy.
        GiveUp,

        /// Kept for a later frame.
        Wait,

        /// At the stove: its menu is hidden (if tweened), and the start
        /// waits a moment after.
        Stove,

        /// Into the dream now.
        Go,
    }
}
