namespace DarkwoodMP.Combat.Enemies
{
    /// What the host knows when a client's EnemyHit request arrives (the
    /// plugin's NetEnemies.OnHit).
    public readonly struct HitFacts
    {
        /// The id names an enemy the host tracks and it still exists.
        public bool Tracked { get; init; }

        /// EnemyTrackRules.InPlay of it.
        public bool InPlay { get; init; }

        /// The sender has a stand-in (NetPlayerProxy.All).
        public bool StandIn { get; init; }
    }
}
