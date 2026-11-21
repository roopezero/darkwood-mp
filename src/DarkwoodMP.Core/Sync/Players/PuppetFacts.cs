namespace DarkwoodMP.Sync.Players
{
    /// A puppet's newest snapshot, as RemotePlayer keeps it (_last).
    public readonly struct PuppetFacts
    {
        /// A snapshot has arrived (_hasLast).
        public bool HasLast { get; init; }

        public bool Alive { get; init; }

        public bool Spectating { get; init; }

        /// In a separate location or a dream.
        public bool Away { get; init; }

        /// The flashlight is on.
        public bool Flashlight { get; init; }

        /// The type of the torch or lamp in hand, or "" (null as "").
        public string HeldLight { get; init; }

        /// The lantern's radius, 0 for none.
        public float Lantern { get; init; }

        /// The beam's radius, 0 when their game sent none.
        public float BeamRadius { get; init; }
    }
}
