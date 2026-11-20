namespace DarkwoodMP.Sync.Players
{
    /// A collider near a spot a player might be placed at
    /// (SpawnSpot.Solid).
    public readonly struct SolidFacts
    {
        /// The collider wasn't destroyed.
        public bool Exists { get; init; }

        public bool Trigger { get; init; }

        /// It already overlaps half a body radius around the wanted spot:
        /// a big volume the spot is inside, not something in the way.
        public bool Background { get; init; }

        /// gameObject.layer.
        public int Layer { get; init; }

        /// Part of the player being placed (transform.IsChildOf).
        public bool OfPlayer { get; init; }

        /// Part of a NetPlayerProxy (GetComponentInParent): those count by
        /// position instead.
        public bool OfProxy { get; init; }
    }
}
