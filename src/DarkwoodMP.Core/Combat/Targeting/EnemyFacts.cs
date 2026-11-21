namespace DarkwoodMP.Combat.Targeting
{
    /// What the threat rules look at about one enemy (the plugin's
    /// Character) on the host.
    public readonly struct EnemyFacts
    {
        /// Threat is on: the host has at least one stand-in
        /// (ThreatRules.IsOn).
        public bool On { get; init; }

        /// The character isn't null.
        public bool Exists { get; init; }

        /// Character.alive.
        public bool Alive { get; init; }

        /// Character.npc isn't null.
        public bool Npc { get; init; }

        /// The host runs it as an enemy (NetEnemies.Eligible).
        public bool Eligible { get; init; }

        /// How many entries its view-range list has
        /// (charactersInViewRange.Count).
        public int Seen { get; init; }

        /// Character.target isn't null.
        public bool HasTarget { get; init; }

        /// That target's CharBase is a player: the host's own or a
        /// stand-in (Proxies.IsPlayer).
        public bool TargetIsPlayer { get; init; }
    }
}
