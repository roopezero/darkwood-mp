namespace DarkwoodMP.World.Throws
{
    /// What a client looks at for an explosion that catches its own
    /// player.
    public readonly struct SelfHitFacts
    {
        /// NetEnemies.ClientMode.
        public bool ClientMode { get; init; }

        /// ThrownHits.Depth.
        public int Depth { get; init; }

        /// A flame is touching something now (ThrownHits.CurrentFlame).
        public bool Flame { get; init; }

        /// Our player is away in a place of their own
        /// (EnemyAreaHits.PlayerAway).
        public bool Away { get; init; }
    }
}
