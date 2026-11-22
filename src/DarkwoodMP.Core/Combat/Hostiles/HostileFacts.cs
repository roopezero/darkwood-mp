namespace DarkwoodMP.Combat.Hostiles
{
    /// What HostileRules looks at in a character (FactsOf(Character)).
    public readonly struct HostileFacts
    {
        /// It is an NPC (Character.npc isn't null).
        public bool IsNpc { get; init; }

        /// It is in play (NetEnemies.InPlay).
        public bool InPlay { get; init; }

        /// Its behaviour is Character.Behaviour.chasingTarget.
        public bool Chasing { get; init; }

        /// Its target has a Player or a NetPlayerProxy.
        public bool TargetIsPlayer { get; init; }

        /// Its aggressiveness is Aggressiveness.attackOnSight.
        public bool AttacksOnSight { get; init; }

        /// Character.attacksFaction(Faction.player).
        public bool AttacksPlayers { get; init; }

        /// It is in HostileNpcs' running set (the host runs it, or a
        /// client shows the host's copy of it).
        public bool Listed { get; init; }
    }
}
