namespace DarkwoodMP.Sync.Players
{
    /// Who sent one of the effect messages, and whom it says it's about
    /// (PlayerFx.Owner).
    public readonly struct OwnerFacts
    {
        /// This game is the host (Session.IsHost).
        public bool Host { get; init; }

        public ulong From { get; init; }

        /// The owner the message says.
        public ulong Said { get; init; }

        /// Session.HostId.
        public ulong HostId { get; init; }

        /// Session.LocalId.
        public ulong LocalId { get; init; }
    }
}
