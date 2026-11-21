namespace DarkwoodMP.World.Story
{
    /// PrologueRules.Update's answer: what to do this frame, and the new
    /// state.
    public readonly struct PrologueStep
    {
        /// The stage to share with the others (DreamSync.SharePrologue);
        /// null for none.
        public string Share { get; init; }

        /// Let the joiner's player go (Core.forbidInputs = false).
        public bool LetGo { get; init; }

        /// Make the joiner's player a first play (Player.firstPlay).
        public bool MakeFirstPlay { get; init; }

        /// The new PrologueFacts.FirstPlaySet.
        public bool FirstPlaySet { get; init; }

        /// The new PrologueFacts.Loaded.
        public bool Loaded { get; init; }

        /// The new PrologueFacts.LoadedAt.
        public Seconds LoadedAt { get; init; }
    }
}
