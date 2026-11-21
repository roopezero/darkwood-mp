namespace DarkwoodMP.World.Hideout
{
    /// After spawnPorter: whether the host tells the others.
    public readonly struct CalledFacts
    {
        /// Before it, spawnPorter was to make the porter's spawner
        /// (PorterRules.SpawnerMade).
        public bool SpawnerMade { get; init; }

        /// The game's spawnPorter ran (CallStep.RunGame).
        public bool RanGame { get; init; }

        /// Playing together, after it.
        public bool Together { get; init; }

        /// This game is the host.
        public bool IsHost { get; init; }

        /// The location has a porterPosition, after it.
        public bool HasPorterPlace { get; init; }
    }
}
