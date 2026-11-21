namespace DarkwoodMP.Combat.Damage
{
    /// What PlayerDamage.Send looks at.
    public readonly struct ProxyHitFacts
    {
        /// The attacker has a Character in its parents whose npc isn't
        /// null (attacker.GetComponentInParent<Character>()).
        public bool ByNpc { get; init; }

        /// That Character is one the host runs as an enemy
        /// (NetEnemies.Eligible).
        public bool NpcIsEnemy { get; init; }

        /// The stand-in's owner is PlayerSync.DummyId.
        public bool ToDummy { get; init; }

        /// ModHost.Session is set and IsHost.
        public bool Host { get; init; }
    }
}
