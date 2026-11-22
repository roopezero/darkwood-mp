namespace DarkwoodMP.World.Throws
{
    /// What ThrownHits.Now looks at.
    public readonly struct ThrownHitFacts
    {
        /// How deep in a thrown thing's impact or explosion this game is
        /// (ThrownHits.Depth).
        public int Depth { get; init; }

        /// A flame is touching something now (ThrownHits.CurrentFlame).
        public bool Flame { get; init; }

        /// That flame, or a parent of it, is something a thrown thing
        /// spawned (ThrownHits.Spawned).
        public bool UnderSpawned { get; init; }
    }
}
