namespace DarkwoodMP.Combat.Damage
{
    /// An explosion the host may show to its clients
    /// (EnemyAreaHits.SendExplosion).
    public readonly struct ExplosionSendFacts
    {
        /// EnemyAreaHits.ByEnemy(the explosion, objectThatSpawnedMe).
        public bool ByEnemy { get; init; }

        /// ModHost.Session is set and IsHost.
        public bool Host { get; init; }

        /// Session.Peers.Count.
        public int Peers { get; init; }

        /// explodeSound isn't null or "".
        public bool Sound { get; init; }

        /// explosionPrefab isn't null and its name isn't "".
        public bool Effect { get; init; }
    }
}
