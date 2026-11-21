namespace DarkwoodMP.Sync.Players
{
    /// A puppet, for whether it has a marker on the world map
    /// (MapMarkers.OnMap).
    public readonly struct MarkerFacts
    {
        /// The tests' dummy (PuppetRules.DummyId).
        public bool Dummy { get; init; }

        /// Its root is there (not destroyed).
        public bool HasRoot { get; init; }

        /// PuppetRules.IsAway: its own, not Whereabouts.SamePlace.
        public bool Away { get; init; }

        /// PuppetRules.IsSpectating.
        public bool Spectating { get; init; }
    }
}
