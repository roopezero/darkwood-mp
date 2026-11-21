namespace DarkwoodMP.Net.Steam
{
    /// What decides whether Steam made us the lobby's owner behind the
    /// host's back (the end of Lobby.OnChatUpdate).
    public readonly struct OwnerFacts
    {
        /// Still in the lobby (Lobby.InLobby), read after any leaving above.
        public bool InLobby { get; init; }

        /// We host it (Lobby._iAmHost).
        public bool IAmHost { get; init; }

        /// Steam's owner now (SteamMatchmaking.GetLobbyOwner).
        public ulong Owner { get; init; }

        /// Our Steam id (SteamUser.GetSteamID).
        public ulong Me { get; init; }
    }
}
