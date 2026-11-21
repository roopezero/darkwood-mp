namespace DarkwoodMP.Net.Steam
{
    /// What decides whether we take a P2P session request
    /// (SteamBackend's ShouldAccept).
    public readonly struct AcceptFacts
    {
        /// We're in a Steam lobby (Lobby.InLobby).
        public bool InLobby { get; init; }

        /// We host it (Lobby.IsHost).
        public bool IsHost { get; init; }

        /// The requester is one of the lobby's members (Lobby.IsMember).
        /// The shell reads it only for a host today.
        public bool Member { get; init; }

        /// The requester is the lobby's host as we entered it
        /// (Lobby.Owner).
        public bool FromHost { get; init; }
    }
}
