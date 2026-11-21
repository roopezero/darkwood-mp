namespace DarkwoodMP.World.Places
{
    /// Location.addObjectsToGrid ran on the host.
    public readonly struct GridFacts
    {
        /// The location loading for the joiners; null for none.
        public string Loading { get; init; }

        /// The location exists.
        public bool HasLocation { get; init; }

        /// Its true name (Core.getTrueLocationName).
        public string TrueName { get; init; }

        /// The grid's name when the load began; null for none.
        public string Before { get; init; }

        /// WorldGrid and its currentGrid exist.
        public bool HasGrid { get; init; }

        /// The current grid's name.
        public string Grid { get; init; }
    }
}
