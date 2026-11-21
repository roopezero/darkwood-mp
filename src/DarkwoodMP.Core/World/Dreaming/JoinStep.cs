namespace DarkwoodMP.World.Dreaming
{
    /// A pending start's step this frame, with its new start time.
    public readonly struct JoinStep
    {
        public JoinAction Action { get; init; }

        /// Not before this; the old value unless Action is Stove.
        public Seconds StartAt { get; init; }
    }
}
