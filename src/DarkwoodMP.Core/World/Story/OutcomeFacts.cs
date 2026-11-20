namespace DarkwoodMP.World.Story
{
    /// EpilogueOutcomes.Start (EpilogueSync.OutcomesStart).
    public readonly struct OutcomeFacts
    {
        /// The EpilogueOutcomes is there.
        public bool Exists { get; init; }

        /// EpilogueSync.Playing.
        public bool Playing { get; init; }

        /// A session is set and Active.
        public bool InSession { get; init; }

        /// This game is the host.
        public bool IsHost { get; init; }

        /// What a joiner heard of the host's outcome.
        public HostBurned Host { get; init; }

        /// Our own EpilogueOutcomes.burnedForest.
        public bool Burned { get; init; }
    }
}
