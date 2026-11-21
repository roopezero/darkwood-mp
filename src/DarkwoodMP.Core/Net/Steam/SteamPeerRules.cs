namespace DarkwoodMP.Net.Steam
{
    /// Who a Steam game talks to: SteamBackend's ShouldAccept, its
    /// OnSessionLost and OnMemberLeft handlers, and Rejoin.
    public static class SteamPeerRules
    {
        /// Why a client leaves when its P2P session with the host fails.
        public const string LostHost = "Lost the connection to the host";

        /// Whether to take a P2P session request: only in a lobby, from a
        /// member for a host, from the host for a client.
        public static Verdict MayAccept(in AcceptFacts peer)
        {
            if (!peer.InLobby)
            {
                return Verdict.Refuse(Refusal.NotInLobby);
            }

            if (peer.IsHost)
            {
                if (peer.Member)
                {
                    return Verdict.Allow;
                }

                return Verdict.Refuse(Refusal.NotInLobby);
            }

            if (peer.FromHost)
            {
                return Verdict.Allow;
            }

            return Verdict.Refuse(Refusal.NotInLobby);
        }

        /// What a failed P2P session means: a host drops a peer that got
        /// in; a client that was let in loses the host; anything else is
        /// ignored.
        public static LostStep Lost(in LostFacts lost)
        {
            if (!lost.InLobby || !lost.HasSession)
            {
                return LostStep.Ignore;
            }

            if (lost.IsHost && lost.KnownPeer)
            {
                return LostStep.DropPeer;
            }

            if (!lost.IsHost && lost.FromHost && lost.Connected)
            {
                return LostStep.LoseHost;
            }

            return LostStep.Ignore;
        }

        /// Whether a member's leaving the lobby closes its session and
        /// raises OnPeerLost: always for a host (whether it got in or not),
        /// only for the host on a client, whose host's PlayerLeft does the
        /// rest.
        public static bool ActsOnLeft(bool isHost, bool fromHost)
        {
            return isHost || fromHost;
        }

        /// Whether "Reload the host's world" rejoins: only a client in a
        /// lobby. It leaves and joins the same lobby again.
        public static bool MayRejoin(bool isHost, bool inLobby)
        {
            return !isHost && inLobby;
        }
    }
}
