namespace DarkwoodMP.World.Story
{
    /// Whether this game can go into the epilogue's dream now
    /// (Chapters.StartEpilogue, EpilogueSync.StartHere).
    public readonly struct StartFacts
    {
        /// The game's Dreams.
        public DreamFacts Dreams { get; init; }

        /// Player.Instance is set.
        public bool HasPlayer { get; init; }

        /// OutsideLocations is there and playerInOutsideLocation.
        public bool InOutside { get; init; }

        /// OutsideLocations is there and loading.
        public bool PlaceLoading { get; init; }

        /// Core.loadingGame.
        public bool Loading { get; init; }
    }
}
