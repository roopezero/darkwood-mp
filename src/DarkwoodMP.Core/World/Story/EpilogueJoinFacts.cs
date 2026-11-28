namespace DarkwoodMP.World.Story
{
    /// The host's epilogue as a new joiner's Hello sees it
    /// (EpilogueSync.JoinRefusal).
    public readonly struct EpilogueJoinFacts
    {
        /// EpilogueSync.Playing.
        public bool Playing { get; init; }

        /// This game is the host.
        public bool Host { get; init; }

        /// The epilogue's outcomes screen has come on (the credits follow).
        public bool AtEnd { get; init; }
    }
}
