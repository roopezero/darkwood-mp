namespace DarkwoodMP.Sync.Players
{
    /// A PlayerState that arrived (PlayerSync.OnPlayerState).
    public readonly struct StateFacts
    {
        /// This game is the host (Session.IsHost).
        public bool Host { get; init; }

        /// Who sent it.
        public ulong From { get; init; }

        /// The owner the message says it's about.
        public ulong Said { get; init; }

        /// Session.HostId.
        public ulong HostId { get; init; }

        /// Session.LocalId.
        public ulong LocalId { get; init; }

        /// The player it's about left less than LeftIgnore ago
        /// (LeftPlayers.JustNow, keyed by PlayerStateRules.GuardKey).
        public bool LeftJustNow { get; init; }

        /// The snapshot says its player is a spectator.
        public bool Spectating { get; init; }
    }
}
