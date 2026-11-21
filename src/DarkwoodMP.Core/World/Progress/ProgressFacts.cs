namespace DarkwoodMP.World.Progress
{
    /// The shared progression and the game's level requirements, as the
    /// host's or a client's game holds them (Progression).
    public readonly struct ProgressFacts
    {
        /// The shared essence (Progression.SharedExp).
        public int SharedExp { get; init; }

        /// The shared level (Progression.SharedLevel).
        public int SharedLevel { get; init; }

        /// Player.levelRequirements: the essence each level needs. Null or
        /// empty counts as 0 for every level.
        public int[] Requirements { get; init; }

        /// Player.Instance is there (MostLevels' p != null).
        public bool HasPlayer { get; init; }
    }
}
