namespace DarkwoodMP.Sync.Players
{
    /// One collider on our player's own object, for its body radius
    /// (PlayerSeparation.Measure: GetComponents, not children).
    public readonly struct ColliderFacts
    {
        /// The collider wasn't destroyed.
        public bool Exists { get; init; }

        public bool Trigger { get; init; }

        public bool Enabled { get; init; }

        /// bounds.extents on the ground plane.
        public Vec2 Extents { get; init; }
    }
}
