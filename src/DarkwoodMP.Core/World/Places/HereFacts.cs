namespace DarkwoodMP.World.Places
{
    /// A client: what decides whether the host runs our player's area.
    public readonly struct HereFacts
    {
        /// Our player dreams (Dreams.dreaming).
        public bool Dreaming { get; init; }

        /// The dream is the shared level dream
        /// (PlaceRules.IsSharedDream).
        public bool SharedDream { get; init; }

        /// We gave up asking where the dream's location stands
        /// (LocationSpots.GaveUpOn, its name and true name).
        public bool DreamGivenUp { get; init; }

        /// Our player is in a separate location (OutsideLocations exists
        /// and playerInOutsideLocation).
        public bool InLocation { get; init; }

        /// We gave up on that location (LocationSpots.GaveUpOn with
        /// currentLocationName, "" for null, and its true name).
        public bool LocationGivenUp { get; init; }
    }
}
