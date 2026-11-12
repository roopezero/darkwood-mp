using System;

namespace DarkwoodMP.Net
{
    /// <summary>
    /// A way of connecting players: Steam (lobbies + P2P relay) or direct (goasis-style TCP + server browser).
    /// Peers are identified by an opaque ulong that only has meaning to the backend that issued it.
    /// Anything Steam-specific must stay inside SteamBackend so the mod still loads on the GOG build.
    /// </summary>
    public interface INetBackend : IDisposable
    {
        string Name { get; }
        string Status { get; }
        bool InSession { get; }
        bool IsHost { get; }
        /// <summary>Client: the transport id of the host.</summary>
        ulong HostId { get; }
        /// <summary>Host: our own id as seen by peers.</summary>
        ulong LocalId { get; }
        /// <summary>Host: the password players must give in Hello ("" for none).</summary>
        string HostPassword { get; }
        /// <summary>Client: the password to send in Hello.</summary>
        string JoinPassword { get; }
        /// <summary>Host: most players at once, the host included.</summary>
        int MaxPlayers { get; }
        /// <summary>Client: the last session ended because the host went away (its connection or lobby), not because we left.</summary>
        bool HostLost { get; }

        /// <summary>Session started: created as host, or reached the host as a client.</summary>
        event Action OnEntered;
        /// <summary>Session ended locally (left, host gone, connection failed).</summary>
        event Action OnLeft;
        /// <summary>A remote peer disconnected.</summary>
        event Action<ulong> OnPeerLost;
        event Action<ulong, byte[], int> OnMessage;

        bool Send(ulong to, byte[] data, int length, int channel, bool reliable);
        /// <summary>Host: close a peer's connection once what was sent to it has gone out.</summary>
        void Kick(ulong peer);
        void Poll();
        void Leave();
        /// <summary>Client: leave and join the same host again (to load its world anew).</summary>
        void Rejoin();
    }
}
