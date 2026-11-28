namespace DarkwoodMP.World.Story
{
    /// The game as the start at the epilogue sees it
    /// (World.Story.EpilogueStart).
    public readonly struct EpilogueStartFacts
    {
        /// The save was started at the epilogue (its SaveBook record says so).
        public bool AtEpilogue { get; init; }

        /// This game is a joiner's.
        public bool Joiner { get; init; }

        /// WorldShare.WorldReady: the world has loaded.
        public bool InWorld { get; init; }

        /// Dreams.dreaming, or a dream on its way (wantToDream).
        public bool Dreaming { get; init; }

        /// Player.inEpilogue, or the shared epilogue (EpilogueSync.Playing).
        public bool InEpilogue { get; init; }

        /// The epilogue was already started in this world.
        public bool Started { get; init; }

        /// How long the world has been ready (0: it isn't).
        public Seconds ReadyFor { get; init; }
    }
}
