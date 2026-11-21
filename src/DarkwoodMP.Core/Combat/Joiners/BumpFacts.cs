namespace DarkwoodMP.Combat.Joiners
{
    /// An enemy touched by a collider (Character.onCollideWith on the
    /// host), for ProxyBumpPatch.
    public readonly struct BumpFacts
    {
        /// The collider isn't null.
        public bool Collider { get; init; }

        /// Threat is on (ThreatRules.IsOn).
        public bool On { get; init; }

        /// This game runs frozen copies of the host's enemies
        /// (NetEnemies.ClientMode).
        public bool ClientMode { get; init; }

        /// Character.isActive.
        public bool Active { get; init; }

        /// The collider is a stand-in's.
        public bool StandIn { get; init; }

        /// The stand-in is alive (CharBase.alive).
        public bool StandInAlive { get; init; }

        /// NetPlayerProxy.Away.
        public bool StandInAway { get; init; }

        /// The stand-in is invisible.
        public bool StandInInvisible { get; init; }

        /// Character.banshee.
        public bool Banshee { get; init; }

        /// Character.sleeping.
        public bool Sleeping { get; init; }

        /// Character.wakeUpOnlyManually.
        public bool WakesOnlyByHand { get; init; }

        /// Character.dieOnContactWithTarget.
        public bool DiesOnContact { get; init; }
    }
}
