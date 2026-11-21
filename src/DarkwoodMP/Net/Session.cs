using System;
using System.Collections.Generic;

namespace DarkwoodMP.Net
{
    /// <summary>
    /// Multiplayer session, independent of how players are connected (Steam or
    /// direct TCP).
    /// Star topology: the host is authoritative, clients only talk to the host,
    /// and the host relays.
    ///
    /// The handshake follows goasis: the client sends Hello (protocol version,
    /// game, player key, password, save hash); the host answers Refused and
    /// closes, or Welcome with the client's id. Ids are the host's transport
    /// ids for its peers; names and keys travel in the handshake, so Steam and
    /// GOG players mix.
    /// </summary>
    public sealed class Session : IDisposable
    {
        public static Session Instance { get; private set; }

        public INetBackend Backend { get; private set; }

        public bool Active => Backend != null && Backend.InSession;
        public bool IsHost => Active && Backend.IsHost;
        public bool IsClient => Active && !Backend.IsHost;
        /// <summary>
        /// Handshake done (always true for the host while hosting).
        /// </summary>
        public bool Connected { get; private set; }
        public ulong LocalId { get; private set; }
        public ulong HostId
        {
            get
            {
                INetBackend backend = Backend;
                if (backend != null)
                {
                    return backend.HostId;
                }
                return 0;
            }
        }
        public string Status
        {
            get
            {
                INetBackend backend = Backend;
                string status = null;
                if (backend != null)
                {
                    status = backend.Status;
                }
                if (status == null)
                {
                    return "Not connected";
                }
                return status;
            }
        }
        public string LocalName = "Player";
        public string LocalKey = "";
        /// <summary>Why the host turned us down last, for the panel.</summary>
        public string LastRefusal;

        /// <summary>Host: clients that completed the handshake.</summary>
        public readonly HashSet<ulong> Peers = new HashSet<ulong>();
        /// <summary>
        /// Everyone in the session we know a name for (including ourselves once
        /// connected).
        /// </summary>
        public readonly Dictionary<ulong, string> Names =
            new Dictionary<ulong, string>();
        /// <summary>
        /// Host: each peer's player key, which the host knows them by.
        /// </summary>
        public readonly Dictionary<ulong, string> Keys =
            new Dictionary<ulong, string>();

        public event Action<ulong> OnPeerConnected;
        public event Action<ulong> OnPeerDisconnected;
        public event Action OnSessionEnded;
        /// <summary>
        /// Client: the host went away (stopped its server, quit, or the
        /// connection broke); the reason to show.
        /// </summary>
        public event Action<string> OnHostLost;
        /// <summary>Client: the host refused us (reason).</summary>
        public event Action<string> OnRefused;
        /// <summary>Client: the host let us in (after Welcome).</summary>
        public event Action OnWelcomed;
        /// <summary>
        /// Someone else joined or left the game (their name), for the in-game
        /// notices.
        /// </summary>
        public event Action<string> OnPlayerJoined, OnPlayerLeft;
        /// <summary>
        /// Someone's Hello was let in and they're getting the world (host:
        /// anyone; client: told by JoinNotice), or they stopped before getting
        /// in.
        /// </summary>
        public event Action<string> OnPlayerJoining, OnPlayerStoppedJoining;
        /// <summary>
        /// Client: joining gave up because the host went quiet (the reason);
        /// the session is left after it.
        /// </summary>
        public event Action<string> OnJoinTimedOut;

        /// <summary>
        /// Client: the hash of the world it has for Hello (null: none).
        /// </summary>
        public Func<byte[]> ClientWorldHash;
        /// <summary>
        /// Host: called once a Hello passes the checks, with the client's world
        /// hash. Return true to let the client in, or false after answering it
        /// yourself (a SaveOffer, or Refuse).
        /// </summary>
        public Func<ulong, byte[], bool> BeforeWelcome;
        /// <summary>
        /// Host: whether a client's message may be handled (false drops it).
        /// Authority sets it.
        /// </summary>
        public Func<ulong, MsgType, bool> AllowBare;

        private readonly Dictionary<MsgType, Action<ulong, PacketReader>>
            _handlers = new Dictionary<MsgType, Action<ulong, PacketReader>>();
        private readonly PacketWriter _pw = new PacketWriter();
        private readonly List<INetBackend> _backends = new List<INetBackend>();
        private float _welcomedAt;
        // Client: when the last Hello went, until anything comes back from the
        // host (-1: nothing outstanding).
        private float _helloAt = -1f;
        private const float HelloRetry = 5f;
        // Client: when the host last sent anything while we're joining. Every
        // stage of joining hears from the host at least every few seconds
        // (JoinWait, SaveOffer, SaveData, Welcome), so this long without a word
        // means it's gone.
        private float _heardHostAt = -1f;
        private const float JoinSilence = 60f;
        // Host: those whose Hello passed the checks but who aren't in yet, by
        // id: announced once (JoinNotice).
        private readonly Dictionary<ulong, string> _joining =
            new Dictionary<ulong, string>();

        private static float Now => UnityEngine.Time.realtimeSinceStartup;

        public Session()
        {
            Instance = this;
            Register(MsgType.Hello, HandleHello);
            Register(MsgType.Welcome, HandleWelcome);
            Register(MsgType.Refused, HandleRefused);
            Register(MsgType.PlayerInfo, (from, r) =>
            {
                if (from != HostId)
                {
                    return;
                }
                ulong id = r.R.ReadUInt64();
                string name = r.ReadStr();
                bool isNew = !Names.ContainsKey(id);
                Names[id] = name;
                // Not the host, nor whoever was already in when we joined
                // (they're listed right after Welcome).
                if (isNew && Connected && id != LocalId && id != HostId
                    && Now - _welcomedAt > 3f)
                {
                    Action<string> handler = OnPlayerJoined;
                    if (handler != null)
                    {
                        handler(name);
                    }
                }
            });
            Register(MsgType.JoinNotice, (from, r) =>
            {
                if (from != HostId || IsHost)
                {
                    return;
                }
                string name = r.ReadStr();
                byte what = r.R.ReadByte();
                if (what == 0)
                {
                    Action<string> joining = OnPlayerJoining;
                    if (joining != null)
                    {
                        joining(name);
                    }
                }
                else
                {
                    Action<string> stopped = OnPlayerStoppedJoining;
                    if (stopped != null)
                    {
                        stopped(name);
                    }
                }
            });
            Register(MsgType.PlayerLeft, (from, r) =>
            {
                if (from != HostId)
                {
                    return;
                }
                ulong who = r.R.ReadUInt64();
                if (Names.TryGetValue(who, out var gone))
                {
                    Action<string> left = OnPlayerLeft;
                    if (left != null)
                    {
                        left(gone);
                    }
                }
                Names.Remove(who);
                Action<ulong> disconnected = OnPeerDisconnected;
                if (disconnected != null)
                {
                    disconnected(who);
                }
            });
        }

        /// <summary>
        /// Register a backend. Whichever backend starts a session becomes the
        /// active one (e.g. accepting a Steam invite while idle on direct);
        /// only one session runs at a time.
        /// </summary>
        public void Add(INetBackend b)
        {
            _backends.Add(b);
            b.OnEntered += () =>
            {
                if (Backend != b)
                {
                    var old = Backend;
                    Backend = b;
                    if (old != null && old.InSession)
                    {
                        old.Leave();
                    }
                }
                OnEntered();
            };
            b.OnLeft += () =>
            {
                if (Backend != b)
                {
                    return;
                }
                EndSession();
                if (b.HostLost)
                {
                    Action<string> handler = OnHostLost;
                    if (handler != null)
                    {
                        handler(b.Status);
                    }
                }
            };
            b.OnPeerLost += id =>
            {
                if (Backend == b)
                {
                    OnPeerLost(id);
                }
            };
            b.OnMessage += (from, data, len) =>
            {
                if (Backend == b)
                {
                    Received(from, data, len);
                }
            };
        }

        // ---- test lag -------------------------------------------------------
        // DWMP_LAG_MS=N in the game's environment holds every message that
        // arrives N ms (in order) before it's handled, as a link over the
        // internet does. Tests only; a localhost run has none.
        private static readonly float LagSecs = ReadLag();
        private readonly Queue<(float at, ulong from, byte[] data)> _late =
            new Queue<(float, ulong, byte[])>();

        private static float ReadLag()
        {
            var v = Environment.GetEnvironmentVariable("DWMP_LAG_MS");
            if (int.TryParse(v, out int ms) && ms > 0)
            {
                return ms / 1000f;
            }
            return 0f;
        }

        private void Received(ulong from, byte[] data, int length)
        {
            if (LagSecs <= 0f)
            {
                Dispatch(from, data, length);
                return;
            }
            var copy = new byte[length];
            Buffer.BlockCopy(data, 0, copy, 0, length);
            _late.Enqueue(
                (UnityEngine.Time.unscaledTime + LagSecs, from, copy));
        }

        private void DeliverLate()
        {
            float now = UnityEngine.Time.unscaledTime;
            while (_late.Count > 0 && _late.Peek().at <= now)
            {
                var m = _late.Dequeue();
                Dispatch(m.from, m.data, m.data.Length);
            }
        }

        public void Register(
            MsgType type, Action<ulong, PacketReader> handler) =>
            _handlers[type] = handler;

        public string NameOf(ulong id)
        {
            if (Names.TryGetValue(id, out var n))
            {
                return n;
            }
            return $"Player {id}";
        }

        public PacketWriter Begin(MsgType type) => _pw.Begin(type);

        /// <summary>Host: everyone playing, the host included.</summary>
        public int PlayerCount
        {
            get
            {
                if (IsHost)
                {
                    return 1 + Peers.Count;
                }
                return Names.Count;
            }
        }

        // ---- sending -------------------------------------------------------

        public void SendToHost(PacketWriter pw, int channel, bool reliable)
        {
            if (IsClient)
            {
                Backend.Send(HostId, pw.Buffer, pw.Length, channel, reliable);
            }
        }

        public void SendTo(
            ulong peer, PacketWriter pw, int channel, bool reliable)
        {
            INetBackend backend = Backend;
            if (backend != null)
            {
                backend.Send(peer, pw.Buffer, pw.Length, channel, reliable);
            }
        }

        /// <summary>
        /// Host: send to every connected client, optionally skipping one (e.g.
        /// the originator).
        /// </summary>
        public void Broadcast(
            PacketWriter pw, int channel, bool reliable, ulong except = 0)
        {
            if (!IsHost)
            {
                return;
            }
            foreach (var p in Peers)
            {
                if (p != except)
                {
                    Backend.Send(p, pw.Buffer, pw.Length, channel, reliable);
                }
            }
        }

        // ---- handshake -----------------------------------------------------

        private void OnEntered()
        {
            Peers.Clear();
            Names.Clear();
            Keys.Clear();
            LastRefusal = null;
            if (IsHost)
            {
                LocalId = Backend.LocalId;
                Names[LocalId] = LocalName;
                Keys[LocalId] = LocalKey;
                Connected = true;
                return;
            }
            Connected = false;
            SendHello();
        }

        /// <summary>
        /// Client: (re)send Hello, e.g. after downloading the host's world.
        /// </summary>
        public void SendHello()
        {
            if (!IsClient)
            {
                return;
            }
            var w = Begin(MsgType.Hello);
            w.W.Write(Protocol.Version);
            w.WriteStr(Protocol.Game);
            w.WriteStr(LocalKey);
            w.WriteStr(Backend.JoinPassword);
            w.WriteStr(PluginInfo.Version);
            w.WriteStr(LocalName);
            Func<byte[]> handler = ClientWorldHash;
            byte[] hash = null;
            if (handler != null)
            {
                hash = handler();
            }
            byte[] b;
            if (hash != null && hash.Length == 32)
            {
                b = hash;
            }
            else
            {
                b = new byte[32];
            }
            w.WriteBytes(b); // the world we have; zeros: none
            SendToHost(w, Channel.Control, reliable: true);
            _helloAt = Now;
            if (_heardHostAt < 0f)
            {
                // the silence counts from the first Hello
                _heardHostAt = _helloAt;
            }
            Plugin.Log.LogInfo("Sent Hello to host");
        }

        private void HandleHello(ulong from, PacketReader r)
        {
            if (!IsHost)
            {
                return;
            }
            // Hello from someone already playing: over Steam a peer's id stays
            // the same when it leaves and joins again (Rejoin), and its new
            // Hello can come before the lobby says it left. Treat it as a new
            // join.
            if (Peers.Contains(from))
            {
                Plugin.Log.LogInfo(
                    $"{NameOf(from)} sent Hello again: joining them anew");
                OnPeerLost(from);
            }
            ushort version;
            string game, key, password, modVersion, name;
            byte[] worldHash;
            try
            {
                version = r.R.ReadUInt16();
                game = r.ReadStr();
                key = r.ReadStr();
                password = r.ReadStr();
                modVersion = r.ReadStr();
                name = Sanitize(r.ReadStr());
                worldHash = r.R.ReadBytes(32);
            }
            catch (Exception)
            {
                Refuse(
                    from,
                    "This isn't a DarkwoodMP client, or it's a very different "
                        + "version.");
                return;
            }

            string why = null;
            if (game != Protocol.Game)
            {
                why = "This server runs a different game.";
            }
            else if (version != Protocol.Version)
            {
                why = $"The host has DarkwoodMP {PluginInfo.Version}, you have "
                    + $"{modVersion}. Use the same version.";
            }
            else if (Backend.HostPassword.Length > 0
                && password != Backend.HostPassword)
            {
                if (password.Length == 0)
                {
                    why = "This server needs a password.";
                }
                else
                {
                    why = "Wrong password.";
                }
            }
            else if (key.Length < 32)
            {
                why = "Bad player key.";
            }
            else if (Keys.TryGetValue(LocalId, out var hostKey)
                && hostKey == key)
            {
                why = "You're already playing on this server.";
            }
            if (why != null)
            {
                Refuse(from, why);
                return;
            }
            // The same player on a new connection: their old one broke without
            // closing (Wi-Fi dropped, a crash). The new one wins, so they don't
            // wait for the old one to time out.
            ulong old = 0;
            foreach (var kv in Keys)
            {
                if (kv.Value == key && kv.Key != from)
                {
                    old = kv.Key;
                    break;
                }
            }
            if (old != 0)
            {
                Plugin.Log.LogInfo(
                    $"{NameOf(old)} (#{old}) joined again as #{from}: dropping "
                        + "the old connection");
                Backend.Kick(old);
                OnPeerLost(old);
            }
            if (PlayerCount >= Backend.MaxPlayers)
            {
                Refuse(from, "The server is full.");
                return;
            }
            // Tell everyone at once that someone is on the way in: getting the
            // world to them can take a while.
            if (!_joining.ContainsKey(from))
            {
                _joining[from] = name;
                Plugin.Log.LogInfo($"{name} (#{from}) is joining");
                Action<string> handler = OnPlayerJoining;
                if (handler != null)
                {
                    handler(name);
                }
                BroadcastJoinNotice(name, 0, except: from);
            }
            if (BeforeWelcome != null && !BeforeWelcome(from, worldHash))
            {
                return;
            }
            _joining.Remove(from);

            var w = Begin(MsgType.Welcome);
            w.W.Write(from); // your id
            SendTo(from, w, Channel.Control, reliable: true);

            // Tell the newcomer about everyone, then everyone about the
            // newcomer.
            foreach (var kv in Names)
            {
                SendTo(
                    from, WritePlayerInfo(kv.Key, kv.Value), Channel.Control,
                    reliable: true);
            }
            Names[from] = name;
            Keys[from] = key;
            Broadcast(
                WritePlayerInfo(from, name), Channel.Control, reliable: true,
                except: from);

            Peers.Add(from);
            Plugin.Log.LogInfo($"Player connected: {name} (#{from})");
            Action<string> joined = OnPlayerJoined;
            if (joined != null)
            {
                joined(name);
            }
            Action<ulong> connected = OnPeerConnected;
            if (connected != null)
            {
                connected(from);
            }
        }

        public void Refuse(ulong peer, string reason)
        {
            Plugin.Log.LogInfo($"Refused #{peer}: {reason}");
            var w = Begin(MsgType.Refused);
            w.WriteStr(reason);
            SendTo(peer, w, Channel.Control, reliable: true);
            Backend.Kick(peer);
            // Refused while joining (BeforeWelcome): the kick raises no
            // peer-lost, so tell everyone they stopped joining.
            if (IsHost && _joining.ContainsKey(peer))
            {
                OnPeerLost(peer);
            }
        }

        /// <summary>
        /// Host: tell the clients someone is joining (what 0) or stopped
        /// joining (what 1).
        /// </summary>
        private void BroadcastJoinNotice(
            string name, byte what, ulong except = 0)
        {
            var w = Begin(MsgType.JoinNotice);
            w.WriteStr(name);
            w.W.Write(what);
            Broadcast(w, Channel.Control, reliable: true, except: except);
        }

        private PacketWriter WritePlayerInfo(ulong id, string name)
        {
            var w = Begin(MsgType.PlayerInfo);
            w.W.Write(id);
            w.WriteStr(name);
            return w;
        }

        private void HandleWelcome(ulong from, PacketReader r)
        {
            if (from != HostId || Connected)
            {
                return;
            }
            LocalId = r.R.ReadUInt64();
            Names[LocalId] = LocalName;
            Connected = true;
            _welcomedAt = Now;
            Plugin.Log.LogInfo($"Joined the host's game as #{LocalId}");
            Action<ulong> handler = OnPeerConnected;
            if (handler != null)
            {
                handler(HostId);
            }
            Action welcomed = OnWelcomed;
            if (welcomed != null)
            {
                welcomed();
            }
        }

        private void HandleRefused(ulong from, PacketReader r)
        {
            if (from != HostId)
            {
                return;
            }
            string reason = r.ReadStr();
            LastRefusal = reason;
            Plugin.Log.LogWarning($"Host refused us: {reason}");
            Action<string> handler = OnRefused;
            if (handler != null)
            {
                handler(reason);
            }
            if (Backend is TcpBackend tcp)
            {
                tcp.Refused(reason);
            }
            else
            {
                Backend.Leave();
            }
        }

        // ---- lifecycle -----------------------------------------------------

        private void OnPeerLost(ulong who)
        {
            if (IsHost && Peers.Remove(who))
            {
                Plugin.Log.LogInfo($"Player left: {NameOf(who)}");
                Action<string> handler = OnPlayerLeft;
                if (handler != null)
                {
                    handler(NameOf(who));
                }
                var w = Begin(MsgType.PlayerLeft);
                w.W.Write(who);
                Broadcast(w, Channel.Control, reliable: true);
            }
            else if (IsHost && _joining.TryGetValue(who, out var was))
            {
                Plugin.Log.LogInfo($"{was} (#{who}) stopped joining");
                Action<string> stopped = OnPlayerStoppedJoining;
                if (stopped != null)
                {
                    stopped(was);
                }
                BroadcastJoinNotice(was, 1);
            }
            _joining.Remove(who);
            Names.Remove(who);
            Keys.Remove(who);
            Action<ulong> disconnected = OnPeerDisconnected;
            if (disconnected != null)
            {
                disconnected(who);
            }
        }

        private void EndSession()
        {
            Peers.Clear();
            Names.Clear();
            Keys.Clear();
            Connected = false;
            _helloAt = -1f;
            _heardHostAt = -1f;
            _late.Clear();
            _joining.Clear();
            Action handler = OnSessionEnded;
            if (handler != null)
            {
                handler();
            }
        }

        /// <summary>
        /// Handle a message as if it had just come from this peer (a Request's
        /// inner message).
        /// </summary>
        public void Handle(ulong from, byte[] data, int length) =>
            Dispatch(from, data, length);

        private void Dispatch(ulong from, byte[] data, int length)
        {
            PacketReader r;
            try
            {
                r = new PacketReader(data, length);
            }
            catch
            {
                return;
            }
            // Only the handshake (and fetching the world it offers) may come
            // from someone not yet admitted.
            if (IsHost && r.Type != MsgType.Hello
                && r.Type != MsgType.NeedSave && !Peers.Contains(from))
            {
                return;
            }
            if (IsHost && AllowBare != null && !AllowBare(from, r.Type))
            {
                return;
            }
            // anything from the host answers our Hello
            if (!IsHost && from == HostId)
            {
                _helloAt = -1f;
                _heardHostAt = Now;
            }
            if (_handlers.TryGetValue(r.Type, out var h))
            {
                try
                {
                    h(from, r);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError(
                        $"Error handling {r.Type} from #{from}: {e}");
                }
            }
            else
            {
                Plugin.Log.LogWarning(
                    $"Unhandled message {r.Type} from #{from}");
            }
        }

        private static string Sanitize(string name)
        {
            if (name == null)
            {
                name = "";
            }
            name = name.Trim();
            if (name.Length > 32)
            {
                name = name.Substring(0, 32);
            }
            if (name.Length == 0)
            {
                return "Player";
            }
            return name;
        }

        public void Update()
        {
            foreach (var b in _backends)
            {
                b.Poll();
            }
            if (_late.Count > 0)
            {
                DeliverLate();
            }
            // A Hello the host never answered (lost, or turned away by a race
            // over Steam): send it again.
            if (IsClient && !Connected && _helloAt >= 0f
                && Now - _helloAt > HelloRetry)
            {
                Plugin.Log.LogInfo(
                    $"No answer to Hello in {HelloRetry:0} s: sending it "
                        + "again");
                SendHello();
            }
            if (IsClient && !Connected && _heardHostAt >= 0f
                && Now - _heardHostAt > JoinSilence)
            {
                string why =
                    $"The host hasn't answered for {JoinSilence:0} s, so "
                        + "joining stopped.";
                Plugin.Log.LogWarning(why);
                _heardHostAt = -1f;
                Action<string> handler = OnJoinTimedOut;
                if (handler != null)
                {
                    handler(why);
                }
                Backend.Leave();
            }
        }

        public void Dispose()
        {
            foreach (var b in _backends)
            {
                b.Dispose();
            }
            _backends.Clear();
            Instance = null;
        }
    }
}
