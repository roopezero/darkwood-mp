namespace DarkwoodMP.Combat.Hostiles
{
    /// Client: our player's hit on an NPC of ours that the host doesn't
    /// run yet (HostileNpcs.ClientHit, from ClientForwardHitPatch).
    public readonly struct NpcHitFacts
    {
        /// We are applying our own hit or attack (HostileNpcs._ours).
        public bool Ours { get; init; }

        /// The character is there and is an NPC.
        public bool IsNpc { get; init; }

        public bool Alive { get; init; }

        /// Character.isNightTrader.
        public bool NightTrader { get; init; }

        /// Only our game has it (NetEnemies.LocalOnly).
        public bool LocalOnly { get; init; }

        /// Something thrown is hitting it now (ThrownHits.Now).
        public bool Thrown { get; init; }

        /// A flame is hitting it now (ThrownHits.CurrentFlame).
        public bool Flame { get; init; }

        /// That flame is our player's (EnemyAreaHits.ByLocalPlayer).
        public bool FlameOurs { get; init; }

        /// getHit's byPlayer.
        public bool ByPlayer { get; init; }

        /// The attacker has a Player.
        public bool AttackerIsPlayer { get; init; }

        public float Damage { get; init; }

        /// How many weapon effects come with it (NetEnemies.HitEffects;
        /// none when null).
        public int Effects { get; init; }
    }
}
