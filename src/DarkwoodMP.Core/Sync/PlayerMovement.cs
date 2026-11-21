namespace DarkwoodMP.Sync
{
    /// What the host remembers about one client's movement, measured over
    /// half a second (kept by MoveTracker).
    public readonly struct PlayerMovement
    {
        /// Where the host let them be. Its y is kept: a
        /// correction sends it, and the others are sent it.
        public Vec3 Accepted { get; init; }

        /// Where the current measurement began, and when.
        public Vec2 MeasureFrom { get; init; }

        public Seconds MeasureAt { get; init; }

        /// When the host last sent them a correction.
        public Seconds PutBackAt { get; init; }
    }
}
