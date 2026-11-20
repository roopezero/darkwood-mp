namespace DarkwoodMP.Combat.Joiners
{
    /// What the host knows about a remote player when it places their
    /// stand-in (Proxies.Update).
    public readonly struct PresenceFacts
    {
        /// This game hosts (Session.IsHost).
        public bool Hosting { get; init; }

        /// RemotePlayer.Away: in a dream or a location of their own.
        public bool Away { get; init; }

        /// RemotePlayer.Alive.
        public bool Alive { get; init; }

        /// RemotePlayer.Last isn't null.
        public bool HasSnapshot { get; init; }

        /// Locations.HostRunsAt(snapshot's place, the stand-in's position)
        /// isn't null: the host runs that location for them. Read only when
        /// Hosting, Away and HasSnapshot are all true.
        public bool HostRuns { get; init; }
    }
}
