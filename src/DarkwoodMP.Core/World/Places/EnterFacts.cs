namespace DarkwoodMP.World.Places
{
    /// Location.enter(force) is about to run on the host.
    public readonly struct EnterFacts
    {
        /// The force argument.
        public bool Force { get; init; }

        /// Location.entered.
        public bool Entered { get; init; }

        /// We keep it running for a joiner.
        public bool Kept { get; init; }

        /// It is marked to skip its next enter events (entered for a
        /// joiner).
        public bool Quiet { get; init; }
    }
}
