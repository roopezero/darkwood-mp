namespace DarkwoodMP.World
{
    /// One id of a HostIds message: the id, and the object the host gave
    /// it to as it stands in the host's game.
    public readonly struct HostIdEntry
    {
        public long Id { get; init; }

        /// The object's name.
        public string Name { get; init; }

        public Vec3 Pos { get; init; }

        /// Its place in its hierarchy (HostIds.PlaceOf).
        public int Place { get; init; }
    }
}
