namespace DarkwoodMP.World.Dreaming
{
    /// What a client does with the host's DreamStart.
    public enum StartStep
    {
        /// Dropped without a word.
        Drop,

        /// Logged and dropped: we are in a dream already.
        AlreadyIn,

        /// Kept as the pending start.
        Pending,
    }
}
