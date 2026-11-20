namespace DarkwoodMP.Combat.Joiners
{
    /// A banshee's sight check on the host (checkIfInSightOfPlayer and
    /// onBansheeOutOfSightOfPlayer).
    public readonly struct BansheeFacts
    {
        /// Threat is on (ThreatRules.IsOn).
        public bool On { get; init; }

        /// This game runs frozen copies (NetEnemies.ClientMode).
        public bool ClientMode { get; init; }

        /// Character.banshee.
        public bool Banshee { get; init; }

        /// Character.alive.
        public bool Alive { get; init; }

        /// The host's player sees it (Player.isInSight).
        public bool HostSees { get; init; }

        /// A stand-in sees it (JoinerSight.AnySees isn't null).
        public bool JoinerSees { get; init; }

        /// Character.target isn't null.
        public bool HasTarget { get; init; }

        /// The target is a stand-in.
        public bool TargetStandIn { get; init; }

        /// That stand-in is alive.
        public bool TargetAlive { get; init; }
    }
}
