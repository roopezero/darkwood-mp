namespace DarkwoodMP.Combat.Landings
{
    /// Client: a message about the host's enemies' throws and landings
    /// came (EnemyProjectile, EnemySensorThrown, EnemyLanded,
    /// EnemyLandedGone).
    public readonly struct HostSendFacts
    {
        /// There is a session.
        public bool HasSession { get; init; }

        /// We are in the host's world (WorldShare.InWorld).
        public bool InWorld { get; init; }

        /// We are a client.
        public bool IsClient { get; init; }

        /// It came from the host.
        public bool FromHost { get; init; }
    }
}
