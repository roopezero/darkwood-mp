namespace DarkwoodMP.World.Story
{
    /// GameEvents.fire has run in our game (EpilogueSync.EventsFired).
    public readonly struct BurnShareFacts
    {
        /// EpilogueSync.Playing.
        public bool Playing { get; init; }

        /// The fire was shared already (EpilogueSync._burnShared).
        public bool Shared { get; init; }

        /// The events' name; null with no events.
        public string Name { get; init; }

        /// WorldActions.Replaying: another game's change being applied.
        public bool Replaying { get; init; }

        /// A session is set and Active.
        public bool InSession { get; init; }
    }
}
