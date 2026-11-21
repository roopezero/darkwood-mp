namespace DarkwoodMP.World.Story
{
    /// An EpilogueStep message, in a game with a session
    /// (EpilogueSync.OnStep).
    public readonly struct EpilogueStepFacts
    {
        /// EpilogueSync.Playing.
        public bool Playing { get; init; }

        /// This game is the host.
        public bool IsHost { get; init; }

        /// The sender is one of the host's peers (Session.Peers).
        public bool FromPeer { get; init; }

        /// The sender is the host (Session.HostId).
        public bool FromHost { get; init; }

        /// The message's step.
        public EpilogueStep Step { get; init; }
    }
}
