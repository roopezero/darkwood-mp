namespace DarkwoodMP.Net.Presence
{
    /// Where our player is, for the presence's biome name
    /// (Plugin.BiomeName).
    public readonly struct WhereFacts
    {
        /// Player.Instance and its whereAmI are there.
        public bool HasPlayer { get; init; }

        /// The player is in a separate location, a cave or an interior
        /// (OutsideLocations.playerInOutsideLocation; false without an
        /// OutsideLocations).
        public bool Outside { get; init; }

        /// whereAmI.currentInfoBiome is there.
        public bool HasBiome { get; init; }

        /// Its type (currentInfoBiome.type), when HasBiome.
        public BiomeKind Kind { get; init; }
    }
}
