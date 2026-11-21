namespace DarkwoodMP.World.Hideout
{
    /// The two hideouts of a porter transport.
    public readonly struct HideoutFacts
    {
        /// The hideout to take things from was found.
        public bool HasSource { get; init; }

        /// Its Location.hideoutId.
        public int SourceId { get; init; }

        /// A big location is where the player is (whereAmI.bigLocation on
        /// the host's own, NightSpawns.BigLocationAt for a request).
        public bool HasTarget { get; init; }

        /// That location is a hideout (Location.playerBase).
        public bool TargetBase { get; init; }

        /// Its hideoutId.
        public int TargetId { get; init; }

        /// The target is the source.
        public bool Same { get; init; }

        /// The target has a porterDeliveryPosition.
        public bool HasPackagePlace { get; init; }

        /// The target's Location name, for the refusal's words.
        public string TargetName { get; init; }
    }
}
