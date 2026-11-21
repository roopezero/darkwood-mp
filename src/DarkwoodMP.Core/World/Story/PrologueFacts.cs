namespace DarkwoodMP.World.Story
{
    /// One frame of the prologue in a session (Prologue.Update).
    public readonly struct PrologueFacts
    {
        /// A session is set and Active, and WorldShare.InWorld.
        public bool InSession { get; init; }

        /// The game's Dreams.
        public DreamFacts Dreams { get; init; }

        /// The dream shared now (DreamSync.Shared).
        public string Shared { get; init; }

        /// This game is a joiner (NetEnemies.ClientMode).
        public bool Joiner { get; init; }

        /// Player.Instance is set.
        public bool HasPlayer { get; init; }

        /// Core.loadingGame.
        public bool Loading { get; init; }

        /// Core.worldGenFinished().
        public bool Generated { get; init; }

        /// Core.forbidInputs.
        public bool Forbidden { get; init; }

        /// DreamSync.InputsHeldByNothing: no cutscene, dialogue or place
        /// holds the inputs.
        public bool HeldByNothing { get; init; }

        /// WorldShare.WorldReady.
        public bool WorldReady { get; init; }

        /// Time.unscaledTime.
        public Seconds Now { get; init; }

        /// The joiner's player was made a first play (Prologue
        /// ._firstPlaySet).
        public bool FirstPlaySet { get; init; }

        /// The load into a stage has finished (Prologue._loadedAt is set).
        public bool Loaded { get; init; }

        /// When it finished, in unscaled time.
        public Seconds LoadedAt { get; init; }
    }
}
