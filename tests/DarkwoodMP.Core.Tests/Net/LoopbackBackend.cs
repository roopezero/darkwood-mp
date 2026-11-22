using System;
using System.Collections.Generic;
using DarkwoodMP.Net;

namespace DarkwoodMP.Tests.Net
{
    /// An INetBackend in memory, for running a host and its clients in one
    /// process. What one backend sends waits in the other's queue until that
    /// one's Poll, so a test decides when each side hears it. Order is kept,
    /// as over TCP: a kick or a leave reaches the other side after what was
    /// sent before it.
    public sealed class LoopbackBackend : INetBackend
    {
        private readonly LoopbackHub _hub;
        private readonly Queue<Action> _inbox = new Queue<Action>();
        // Host: the clients connected to it.
        private readonly HashSet<ulong> _links = new HashSet<ulong>();
        // Client: whether the connection to the host is open.
        private bool _linked;
        private string _lastPassword;

        internal LoopbackBackend(LoopbackHub hub)
        {
            _hub = hub;
        }

        public string Name => "Loopback";
        public string Status { get; private set; } = "Not connected";
        public bool InSession { get; private set; }
        public bool IsHost { get; private set; }
        public ulong HostId => LoopbackHub.HostId;

        public ulong LocalId
        {
            get
            {
                if (IsHost)
                {
                    return LoopbackHub.HostId;
                }

                return 0;
            }
        }

        public string HostPassword { get; private set; } = "";
        public string JoinPassword { get; private set; } = "";
        public int MaxPlayers { get; private set; } = Protocol.MaxPlayers;
        public bool HostLost { get; private set; }

        /// The id the host knows this backend by (the host: 1).
        public ulong Id { get; private set; }

        /// What waits for this backend's next Poll.
        public int Waiting => _inbox.Count;

        /// Host: the clients connected now.
        public IReadOnlyCollection<ulong> Links => _links;

        public event Action OnEntered;
        public event Action OnLeft;
        public event Action<ulong> OnPeerLost;
        public event Action<ulong, byte[], int> OnMessage;

        public bool Host(string password, int maxPlayers)
        {
            if (InSession || _hub.Hosting != null)
            {
                return false;
            }

            Id = LoopbackHub.HostId;
            HostPassword = password;
            MaxPlayers = maxPlayers;
            IsHost = true;
            InSession = true;
            HostLost = false;
            Status = "Hosting";
            _hub.Hosting = this;
            Raise(OnEntered);
            return true;
        }

        /// Connect to whoever is hosting. As over TCP, OnEntered comes on
        /// the next Poll, or OnLeft if nobody hosts.
        public void Join(string password)
        {
            if (InSession)
            {
                return;
            }

            _lastPassword = password;
            JoinPassword = password;
            IsHost = false;
            HostLost = false;
            InSession = true;
            Status = "Connecting";
            Id = _hub.NextClientId();
            var host = _hub.Hosting;
            if (host == null)
            {
                _inbox.Enqueue(() => Shutdown("Couldn't connect: no host"));
                return;
            }

            host._links.Add(Id);
            _linked = true;
            _inbox.Enqueue(() =>
            {
                if (InSession && _linked)
                {
                    Status = "Connected";
                    Raise(OnEntered);
                }
            });
        }

        /// The connection breaks without either side closing it: each side
        /// hears it on its next Poll.
        public void Drop()
        {
            if (IsHost || !_linked)
            {
                return;
            }

            TellHostLost(this);
            ulong id = Id;
            var host = _hub.Hosting;
            if (host != null)
            {
                host._links.Remove(id);
                TellPeerLost(host, id);
            }
        }

        public bool Send(
            ulong to, byte[] data, int length, int channel, bool reliable)
        {
            var target = Target(to);
            if (target == null)
            {
                return false;
            }

            var copy = new byte[length];
            Buffer.BlockCopy(data, 0, copy, 0, length);
            ulong from = Id;
            target._inbox.Enqueue(() =>
            {
                if (target.InSession && target.LinkedTo(from))
                {
                    var handler = target.OnMessage;
                    if (handler != null)
                    {
                        handler(from, copy, copy.Length);
                    }
                }
            });

            return true;
        }

        public void Kick(ulong peer)
        {
            if (!IsHost || !_links.Remove(peer))
            {
                return;
            }

            var client = _hub.Find(peer);
            if (client != null)
            {
                TellHostLost(client);
            }
        }

        public void Poll()
        {
            int n = _inbox.Count;
            for (int i = 0; i < n && _inbox.Count > 0; i++)
            {
                _inbox.Dequeue()();
            }
        }

        public void Leave() => Shutdown("Not connected");

        public void Rejoin()
        {
            if (IsHost || _lastPassword == null)
            {
                return;
            }

            string password = _lastPassword;
            Leave();
            Join(password);
        }

        public void Dispose() => Leave();

        private bool LinkedTo(ulong from)
        {
            if (IsHost)
            {
                return _links.Contains(from);
            }

            return _linked && from == LoopbackHub.HostId;
        }

        private LoopbackBackend Target(ulong to)
        {
            if (!InSession)
            {
                return null;
            }

            if (IsHost)
            {
                if (!_links.Contains(to))
                {
                    return null;
                }

                return _hub.Find(to);
            }

            if (!_linked || to != LoopbackHub.HostId)
            {
                return null;
            }

            return _hub.Hosting;
        }

        private static void TellHostLost(LoopbackBackend client)
        {
            client._inbox.Enqueue(() =>
            {
                if (client.InSession && client._linked)
                {
                    client._linked = false;
                    client.HostLost = true;
                    client.Shutdown(
                        "Disconnected from host: connection closed");
                }
            });
        }

        private static void TellPeerLost(LoopbackBackend host, ulong id)
        {
            host._inbox.Enqueue(() =>
            {
                if (host.InSession && host.IsHost)
                {
                    var handler = host.OnPeerLost;
                    if (handler != null)
                    {
                        handler(id);
                    }
                }
            });
        }

        private void Shutdown(string status)
        {
            bool was = InSession;
            if (IsHost)
            {
                foreach (var id in _links)
                {
                    var client = _hub.Find(id);
                    if (client != null)
                    {
                        TellHostLost(client);
                    }
                }

                _links.Clear();
                _hub.Hosting = null;
            }
            else if (_linked)
            {
                _linked = false;
                var host = _hub.Hosting;
                if (host != null && host._links.Remove(Id))
                {
                    TellPeerLost(host, Id);
                }
            }

            InSession = false;
            IsHost = false;
            Status = status;
            if (was)
            {
                Raise(OnLeft);
            }
        }

        private static void Raise(Action handler)
        {
            if (handler != null)
            {
                handler();
            }
        }
    }
}
