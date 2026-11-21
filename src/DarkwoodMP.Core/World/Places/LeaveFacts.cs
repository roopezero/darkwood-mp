namespace DarkwoodMP.World.Places
{
    /// Location.leave(force) is about to run on the host.
    public readonly struct LeaveFacts
    {
        /// We keep it running for a joiner.
        public bool Kept { get; init; }

        /// The force argument.
        public bool Force { get; init; }

        /// Location.entered.
        public bool Entered { get; init; }

        /// Core.loadingGame.
        public bool LoadingGame { get; init; }

        /// Dreams.dreaming (false with no Dreams).
        public bool Dreaming { get; init; }

        /// Dreams.wantToDream.
        public bool WantToDream { get; init; }
    }
}
