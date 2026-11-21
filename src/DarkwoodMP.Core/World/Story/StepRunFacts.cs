namespace DarkwoodMP.World.Story
{
    /// What decides whether a story step runs in this game.
    public readonly struct StepRunFacts
    {
        /// A session is on (Session.Instance is set and Active).
        public bool InSession { get; init; }

        /// This game is a joiner (Session.IsClient).
        public bool IsClient { get; init; }

        /// EventParts.Current is PlayerAndShared: our own player's
        /// event.
        public bool OwnPart { get; init; }

        /// EventParts.Current is All.
        public bool AllParts { get; init; }

        /// The step's kind (StoryRequestRules.KindOf).
        public StoryKind Kind { get; init; }
    }
}
