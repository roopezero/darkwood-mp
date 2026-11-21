namespace DarkwoodMP.World.Hideout
{
    /// Location.spawnPorter (the porter whistle) is about to run.
    public readonly struct CallFacts
    {
        /// Playing together (TogetherRules.Together).
        public bool Together { get; init; }

        /// Coop.Guards.Replaying.
        public bool Replaying { get; init; }

        /// The location has a porterPosition.
        public bool HasPorterPlace { get; init; }

        /// This game is the host (Session.IsHost).
        public bool IsHost { get; init; }
    }
}
