namespace DarkwoodMP.Combat.Joiners
{
    /// The forest spirit's teleport on the host (Character.waitToTeleport,
    /// CharacterWaitToTeleport).
    public readonly struct TeleportFacts
    {
        /// Threat is on (ThreatRules.IsOn).
        public bool On { get; init; }

        /// This game runs frozen copies (NetEnemies.ClientMode).
        public bool ClientMode { get; init; }

        /// Character.target isn't null.
        public bool HasTarget { get; init; }

        /// The host's player or any stand-in sees the spirit
        /// (JoinerSight.AnyPlayerSees). Read only with a target.
        public bool Seen { get; init; }

        /// The nearest player's distance (JoinerSightRules.Nearest). Read
        /// only with a target nobody sees.
        public float Nearest { get; init; }

        /// The target's position.
        public Vec3 TargetAt { get; init; }

        /// The target's up.
        public Vec3 TargetUp { get; init; }

        /// The host's roll for x: UnityEngine.Random.Range(-100, 100), an
        /// int from -100 to 99.
        public int RollX { get; init; }

        /// The host's roll for z, the same way.
        public int RollZ { get; init; }
    }
}
