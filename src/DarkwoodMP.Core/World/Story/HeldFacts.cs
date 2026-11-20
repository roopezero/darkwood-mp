namespace DarkwoodMP.World.Story
{
    /// What decides the held story step's fate this frame
    /// (Chapters.UpdateHeld).
    public readonly struct HeldFacts
    {
        /// This game is the host and the step's player is one of its
        /// peers.
        public bool PlayerHere { get; init; }

        /// StoryRules.Decide's outcome for it now (ignored when the player
        /// isn't here).
        public StoryOutcome Outcome { get; init; }

        /// It could run on every frame since ReadySince
        /// (Chapters._readySince is set).
        public bool Ready { get; init; }

        /// When it first could, in realtime.
        public Seconds ReadySince { get; init; }

        /// Time.realtimeSinceStartup.
        public Seconds Now { get; init; }
    }
}
