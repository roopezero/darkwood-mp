namespace DarkwoodMP.Net.Steam
{
    /// What decides what a failed P2P session means (SteamBackend's
    /// OnSessionLost handler).
    public readonly struct LostFacts
    {
        /// We're in a Steam lobby (Lobby.InLobby).
        public bool InLobby { get; init; }

        /// A Session exists (Session.Instance isn't null).
        public bool HasSession { get; init; }

        /// We host the lobby (Lobby.IsHost).
        public bool IsHost { get; init; }

        /// The peer got into the game (in Session.Peers).
        public bool KnownPeer { get; init; }

        /// The peer is the lobby's host as we entered it (Lobby.Owner).
        public bool FromHost { get; init; }

        /// The host let us in (Session.Connected).
        public bool Connected { get; init; }
    }
}
