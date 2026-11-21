namespace DarkwoodMP.World.Places
{
    /// The host, answering a joiner's LocationAsk.
    public readonly struct AskFacts
    {
        /// There is a session and this game is its host.
        public bool IsHost { get; init; }

        /// WorldShare.InWorld.
        public bool InWorld { get; init; }

        /// Singleton<OutsideLocations>.Instance exists.
        public bool HasLocations { get; init; }

        /// The name asked for.
        public string Name { get; init; }

        /// A spot is known for it (LocationSpots.TryGet).
        public bool Known { get; init; }

        /// It is in spawnedLocations by that name, not null.
        public bool Loaded { get; init; }

        /// It is a location of the build (LocationNames.IsReal).
        public bool Real { get; init; }
    }
}
