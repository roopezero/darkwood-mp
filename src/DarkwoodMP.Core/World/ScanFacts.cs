namespace DarkwoodMP.World
{
    /// What the search for a Placed object looks at to scan again.
    public readonly struct ScanFacts
    {
        /// The table gave an object, on its spot, with the component.
        public bool Found { get; init; }

        public bool OnSpot { get; init; }

        public bool HasComponent { get; init; }

        /// Time.unscaledTime, and when the table was filled.
        public Seconds Now { get; init; }

        public Seconds ScannedAt { get; init; }
    }
}
