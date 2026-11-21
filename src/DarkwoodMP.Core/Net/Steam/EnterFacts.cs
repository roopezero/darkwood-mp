namespace DarkwoodMP.Net.Steam
{
    /// What Steam tells of a lobby we entered, and what we knew then
    /// (Lobby.OnLobbyEntered). Steam ids are CSteamID.m_SteamID; 0 is
    /// CSteamID.Nil.
    public readonly struct EnterFacts
    {
        /// Steam's call failed (the call result's ioFailure).
        public bool IoFailure { get; init; }

        /// m_EChatRoomEnterResponse is
        /// k_EChatRoomEnterResponseSuccess.
        public bool Success { get; init; }

        /// The lobby entered (m_ulSteamIDLobby).
        public ulong Lobby { get; init; }

        /// The lobby we're in already (Lobby.Id), 0 for none.
        public ulong Current { get; init; }

        /// The lobby we last left for its version (Lobby._refused), 0 for
        /// none.
        public ulong Refused { get; init; }

        /// The lobby's "proto" value, the host's Protocol.Version as text;
        /// empty when it has none.
        public string Proto { get; init; }

        /// Our Protocol.Version.
        public int Ours { get; init; }
    }
}
