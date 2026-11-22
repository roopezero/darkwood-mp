namespace DarkwoodMP.World
{
    /// What tying an object to an id the host gave out looks at.
    public readonly struct TieFacts
    {
        /// The id, and whether the object is alive.
        public long Id { get; init; }

        public bool Exists { get; init; }

        /// The id is tied to a live object, and whether to this one.
        public bool IdTied { get; init; }

        public bool IdTiedHere { get; init; }

        /// The object is tied to an id, and which.
        public bool ObjectTied { get; init; }

        public long ObjectId { get; init; }
    }
}
