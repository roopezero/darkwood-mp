namespace DarkwoodMP.Net.Steam
{
    /// What to do about a failed P2P session.
    public enum LostStep : byte
    {
        /// Nothing: no lobby or session, or a peer that never got in (a
        /// session can fail while it's set up, and Hello is sent again).
        Ignore,

        /// Host: log "Lost the connection to #{id}", close the session and
        /// raise OnPeerLost.
        DropPeer,

        /// Client: Lobby.LoseHost(SteamPeerRules.LostHost).
        LoseHost,
    }
}
