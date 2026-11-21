namespace DarkwoodMP.World.Places
{
    /// Where this game's player is, as PlaceRules.Current reads it.
    public readonly struct WhereFacts
    {
        /// Our player dreams (Dreams.dreaming).
        public bool Dreaming { get; init; }

        /// Our player is in a separate location
        /// (OutsideLocations.playerInOutsideLocation).
        public bool InLocation { get; init; }

        /// The current grid's name (WorldGrid.currentGrid.name); null
        /// when there is no grid.
        public string Grid { get; init; }
    }
}
