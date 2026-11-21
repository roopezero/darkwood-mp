namespace DarkwoodMP.Combat.Landings
{
    /// Host: a throw, a ranged attack or a landing starting, in a patch's
    /// prefix (EnemyThrowPatch, EnemySensorThrowPatch, EnemyLandingPatch
    /// with EnemyLandings.ByHostEnemy).
    public readonly struct ThrowWatchFacts
    {
        /// One of the same kind is being noted already (its static isn't
        /// null): only the outermost is noted.
        public bool Busy { get; init; }

        /// What it starts from is there: the landing thing's thrower
        /// (objectThatSpawnedMe, a Character), or the ranged attack's
        /// sensor type. Always true for spawnProjectile.
        public bool HasSource { get; init; }

        /// We host a session.
        public bool Host { get; init; }

        /// How many peers are connected.
        public int Peers { get; init; }

        /// Coop.Guards.Sharing.
        public bool Sharing { get; init; }

        /// The thrower is one of the host's tracked enemies
        /// (NetEnemies.NetIdOf).
        public bool Tracked { get; init; }
    }
}
