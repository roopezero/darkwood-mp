namespace DarkwoodMP.Combat.Targeting
{
    /// What ThreatRules.Nearest looks at, besides the stand-ins.
    public readonly struct NearestFacts
    {
        /// Threat is on (ThreatRules.IsOn).
        public bool On { get; init; }

        /// Player.Instance isn't null.
        public bool HasHost { get; init; }

        /// The host's player's position, alive or not.
        public Vec3 HostAt { get; init; }

        /// From the enemy to the host's player (Core.trueDistance).
        public float HostDistance { get; init; }

        /// The enemy isn't null.
        public bool Exists { get; init; }

        /// The enemy's position.
        public Vec3 EnemyAt { get; init; }
    }
}
