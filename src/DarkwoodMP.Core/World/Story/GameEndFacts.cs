namespace DarkwoodMP.World.Story
{
    /// A GameEnd message on a joiner (Chapters.OnGameEnd, then
    /// EpilogueSync.OnGameEnd).
    public readonly struct GameEndFacts
    {
        /// A session is set, this game isn't its host, and the message is
        /// from the host.
        public bool FromHost { get; init; }

        /// The message's kind byte (EndKind).
        public byte Kind { get; init; }

        /// The message's phase: 0 the epilogue begins, 1 the session ends.
        public byte Phase { get; init; }

        /// EpilogueSync.Playing.
        public bool Playing { get; init; }

        /// The active scene is "credits".
        public bool AtCredits { get; init; }

        /// WorldShare.InWorld.
        public bool InWorld { get; init; }

        /// Player.Instance is set and inEpilogue.
        public bool InEpilogue { get; init; }
    }
}
