namespace DarkwoodMP.World.Throws
{
    /// What ThrowSync.Thrown and Dropped look at when our player throws or
    /// drops something.
    public readonly struct ThrowFacts
    {
        /// Coop.Guards.Sharing: connected, in a world, not replaying.
        public bool Sharing { get; init; }

        /// The held thing's GameObject is there.
        public bool Held { get; init; }

        /// Its item type (currentItem.baseClass.type); null for none.
        public string Type { get; init; }

        /// It has a ThrownItem.
        public bool Thrown { get; init; }

        /// It has a Rigidbody.
        public bool Body { get; init; }
    }
}
