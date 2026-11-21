namespace DarkwoodMP.Combat.Targeting
{
    /// A player an enemy may be sent after as "the player"
    /// (ThreatRules.Best): the host's own, then each stand-in in the
    /// registry's order. The shell leaves out a null one.
    public readonly struct PlayerCandidate
    {
        /// The host's own player (Player.Instance), not a stand-in.
        public bool Host { get; init; }

        /// CharBase.alive.
        public bool Alive { get; init; }

        /// Player.dying; read only for the host.
        public bool Dying { get; init; }

        /// CharBase.invisible; read only for a stand-in.
        public bool Invisible { get; init; }

        /// The enemy's stored threat for them, after the decay.
        public float Stored { get; init; }

        /// From the enemy, in 3D (Vector3.Distance).
        public float Distance { get; init; }
    }
}
