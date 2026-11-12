using System;
using System.Collections.Generic;

namespace DarkwoodMP.Net
{
    /// <summary>
    /// Multiplayer session, independent of how players are connected (Steam or direct TCP).
    /// Star topology: the host is authoritative, clients only talk to the host, and the host relays.
    ///
    /// The handshake follows goasis: the client sends Hello (protocol version, game, player key, password,
    /// save hash); the host answers Refused and closes, or Welcome with the client's id. Ids are the host's
    /// transport ids for its peers; names and keys travel in the handshake, so Steam and GOG players mix.
    /// </summary>
    public sealed class Session : IDisposable
    {
        public static Session Instance { get; private set; }

        public INetBackend Backend { get; private set; }

        public bool Active => Backend != null && Backend.InSession;
        public bool IsHost => Active && Backend.IsHost;
        public bool IsClient => Active && !Backend.IsHost;
        /// <summary>Handshake done (always true for the host while hosting).</summary>
        public bool Connected { get; private set; }
        public ulong LocalId { get; private set; }
        public ulong HostId => Backend?.HostId ?? 0;
        public string Status => Backend?.Status ?? "Not connected";
        public string LocalName = "Player";
        public string LocalKey = "";
        /// <summary>Why the host turned us down last, for the panel.</summary>
        public string LastRefusal;

        /// <summary>Host: clients that completed the handshake.</summary>
        public readonly HashSet<ulong> Peers = new HashSet<ulong>();
        /// <summary>Everyone in the session we know a name for (including ourselves once connected).</summary>
        public readonly Dictionary<ulong, string> Names = new Dictionary<ulong, string>();
        /// <summary>Host: each peer's player key, which the host knows them by.</summary>
        public readonly Dictionary<ulong, string> Keys = new Dictionary<ulong, string>();

        public event Action<ulong> OnPeerConnected;
        public event Action<ulong> OnPeerDisconnected;
        public event Action OnSessionEnded;
        /// <summary>Client: the host went away (stopped its server, quit, or the connection broke); the reason to show.</summary>
        public event Action<string> OnHostLost;
        /// <summary>Client: the host refused us (reason).</summary>
        public event Action<string> OnRefused;
        /// <summary>Client: the host let us in (after Welcome).</summary>
        public event Action OnWelcomed;
        /// <summary>Someone else joined or left the game (their name), for the in-game notices.</summary>
        public event Action<string> OnPlayerJoined, OnPlayerLeft;
        /// <summary>Someone's Hello was let in and they're getting the world (host: anyone; client: told by JoinNotice), or they stopped before getting in.</summary>
        public event Action<string> OnPlayerJoining, OnPlayerStoppedJoining;
        /// <summary>Client: joining gave up because the host went quiet (the reason); the session is left after it.</summary>
        public event Action<string> OnJoinTimedOut;

        /// <summary>Client: the hash of the world it has for Hello (null: none).</summary>
        public Func<byte[]> ClientWorldHash;
        /// <summary>
        /// Host: called once a Hello passes the checks, with the client's world hash. Return true to let the client
        /// in, or false after answering it yourself (a SaveOffer, or Refuse).
        /// </summary>
        public Func<ulong, byte[], bool> BeforeWelcome;

        private readonly Dictionary<MsgType, Action<ulong, PacketReader>> _handlers = new Dictionary<MsgType, Action<ulong, PacketReader>>();
        private readonly PacketWriter _pw = new PacketWriter();
        private readonly List<INetBackend> _backends = new List<INetBackend>();
        private float _welcomedAt;
        // Client: when the last Hello went, until anything comes back from the host (-1: nothing outstanding).
        private float _helloAt = -1f;
        private const float HelloRetry = 5f;
        // Client: when the host last sent anything while we're joining. Every stage of joining hears from the host at
        // least every few seconds (JoinWait, SaveOffer, SaveData, Welcome), so this long without a word means it's gone.
        private float _heardHostAt = -1f;
        private const float JoinSilence = 60f;
        // Host: those whose Hello passed the checks but who aren't in yet, by id: announced once (JoinNotice).
        private readonly Dictionary<ulong, string> _joining = new Dictionary<ulong, string>();

        public Session()
        {
            Instance = this;
            Register(MsgType.Hello, HandleHello);
            Register(MsgType.Welcome, HandleWelcome);
            Register(MsgType.Refused, HandleRefused);
            Register(MsgType.PlayerInfo, (from, r) =>
            {
                if (from != HostId) return;
                ulong id = r.R.ReadUInt64();
                string name = r.ReadStr();
                bool isNew = !Names.ContainsKey(id);
                Names[id] = name;
                // Not the host, nor whoever was already in when we joined (they're listed right after Welcome).
                if (isNew && Connected && id != LocalId && id != HostId && UnityEngine.Time.realtimeSinceStartup - _welcomedAt > 3f) OnPlayerJoined?.Invoke(name);
            });
            Register(MsgType.JoinNotice, (from, r) =>
            {
                if (from != HostId || IsHost) return;
                string name = r.ReadStr();
                byte what = r.R.ReadByte();
                if (what == 0) OnPlayerJoining?.Invoke(name); else OnPlayerStoppedJoining?.Invoke(name);
            });
            Register(MsgType.PlayerLeft, (from, r) =>
            {
                if (from != HostId) return;
                ulong who = r.R.ReadUInt64();
                if (Names.TryGetValue(who, out var gone)) OnPlayerLeft?.Invoke(gone);
                Names.Remove(who);
                OnPeerDisconnected?.Invoke(who);
            });
        }

        /// <summary>
        /// Register a backend. Whichever backend starts a session becomes the active one (e.g. accepting a
        /// Steam invite while idle on direct); only one session runs at a time.
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
                    if (old != null && old.InSession) old.Leave();
                }
                OnEntered();
            };
            b.OnLeft += () =>
            {
                if (Backend != b) return;
                EndSession();
                if (b.HostLost) OnHostLost?.Invoke(b.Status);
            };
            b.OnPeerLost += id => { if (Backend == b) OnPeerLost(id); };
            b.OnMessage += (from, data, len) => { if (Backend == b) Dispatch(from, data, len); };
        }

        public void Register(MsgType type, Action<ulong, PacketReader> handler) => _handlers[type] = handler;

        public string NameOf(ulong id) => Names.TryGetValue(id, out var n) ? n : $"Player {id}";

        public PacketWriter Begin(MsgType type) => _pw.Begin(type);

        /// <summary>Host: everyone playing, the host included.</summary>
        public int PlayerCount => IsHost ? 1 + Peers.Count : Names.Count;

        // ---- sending -------------------------------------------------------

        public void SendToHost(PacketWriter pw, int channel, bool reliable)
        {
            if (IsClient) Backend.Send(HostId, pw.Buffer, pw.Length, channel, reliable);
        }

        public void SendTo(ulong peer, PacketWriter pw, int channel, bool reliable) =>
            Backend?.Send(peer, pw.Buffer, pw.Length, channel, reliable);

        /// <summary>Host: send to every connected client, optionally skipping one (e.g. the originator).</summary>
        public void Broadcast(PacketWriter pw, int channel, bool reliable, ulong except = 0)
        {
            if (!IsHost) return;
            foreach (var p in Peers)
                if (p != except) Backend.Send(p, pw.Buffer, pw.Length, channel, reliable);
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

        /// <summary>Client: (re)send Hello, e.g. after downloading the host's world.</summary>
        public void SendHello()
        {
            if (!IsClient) return;
            var w = Begin(MsgType.Hello);
            w.W.Write(Protocol.Version);
            w.WriteStr(Protocol.Game);
            w.WriteStr(LocalKey);
            w.WriteStr(Backend.JoinPassword);
            w.WriteStr(PluginInfo.Version);
            w.WriteStr(LocalName);
            var hash = ClientWorldHash?.Invoke();
            w.WriteBytes(hash != null && hash.Length == 32 ? hash : new byte[32]); // the world we have; zeros: none
            SendToHost(w, Channel.Control, reliable: true);
            _helloAt = UnityEngine.Time.realtimeSinceStartup;
            if (_heardHostAt < 0f) _heardHostAt = _helloAt; // the silence counts from the first Hello
            Plugin.Log.LogInfo("Sent Hello to host");
        }

        private void HandleHello(ulong from, PacketReader r)
        {
            if (!IsHost) return;
            // Hello from someone already playing: over Steam a peer's id stays the same when it leaves and joins
            // again (Rejoin), and its new Hello can come before the lobby says it left. Treat it as a new join.
            if (Peers.Contains(from))
            {
                Plugin.Log.LogInfo($"{NameOf(from)} sent Hello again: joining them anew");
                OnPeerLost(from);
            }
            ushort version; string game, key, password, modVersion, name; byte[] worldHash;
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
                Refuse(from, "This isn't a DarkwoodMP client, or it's a very different version.");
                return;
            }

            string why =
                game != Protocol.Game ? "This server runs a different game."
                : version != Protocol.Version ? $"The host has DarkwoodMP {PluginInfo.Version}, you have {modVersion}. Use the same version."
                : Backend.HostPassword.Length > 0 && password != Backend.HostPassword ? (password.Length == 0 ? "This server needs a password." : "Wrong password.")
                : key.Length < 32 ? "Bad player key."
                : Keys.TryGetValue(LocalId, out var hostKey) && hostKey == key ? "You're already playing on this server."
                : null;
            if (why != null) { Refuse(from, why); return; }
            // The same player on a new connection: their old one broke without closing (Wi-Fi dropped, a crash).
            // The new one wins, so they don't wait for the old one to time out.
            ulong old = 0;
            foreach (var kv in Keys) if (kv.Value == key && kv.Key != from) { old = kv.Key; break; }
            if (old != 0)
            {
                Plugin.Log.LogInfo($"{NameOf(old)} (#{old}) joined again as #{from}: dropping the old connection");
                Backend.Kick(old);
                OnPeerLost(old);
            }
            if (PlayerCount >= Backend.MaxPlayers) { Refuse(from, "The server is full."); return; }
            // Tell everyone at once that someone is on the way in: getting the world to them can take a while.
            if (!_joining.ContainsKey(from))
            {
                _joining[from] = name;
                Plugin.Log.LogInfo($"{name} (#{from}) is joining");
                OnPlayerJoining?.Invoke(name);
                var jn = Begin(MsgType.JoinNotice);
                jn.WriteStr(name);
                jn.W.Write((byte)0);
                Broadcast(jn, Channel.Control, reliable: true, except: from);
            }
            if (BeforeWelcome != null && !BeforeWelcome(from, worldHash)) return;
            _joining.Remove(from);

            var w = Begin(MsgType.Welcome);
            w.W.Write(from); // your id
            SendTo(from, w, Channel.Control, reliable: true);

            // Tell the newcomer about everyone, then everyone about the newcomer.
            foreach (var kv in Names) SendTo(from, WritePlayerInfo(kv.Key, kv.Value), Channel.Control, reliable: true);
            Names[from] = name;
            Keys[from] = key;
            Broadcast(WritePlayerInfo(from, name), Channel.Control, reliable: true, except: from);

            Peers.Add(from);
            Plugin.Log.LogInfo($"Player connected: {name} (#{from})");
            OnPlayerJoined?.Invoke(name);
            OnPeerConnected?.Invoke(from);
        }

        public void Refuse(ulong peer, string reason)
        {
            Plugin.Log.LogInfo($"Refused #{peer}: {reason}");
            var w = Begin(MsgType.Refused);
            w.WriteStr(reason);
            SendTo(peer, w, Channel.Control, reliable: true);
            Backend.Kick(peer);
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
            if (from != HostId || Connected) return;
            LocalId = r.R.ReadUInt64();
            Names[LocalId] = LocalName;
            Connected = true;
            _welcomedAt = UnityEngine.Time.realtimeSinceStartup;
            Plugin.Log.LogInfo($"Joined the host's game as #{LocalId}");
            OnPeerConnected?.Invoke(HostId);
            OnWelcomed?.Invoke();
        }

        private void HandleRefused(ulong from, PacketReader r)
        {
            if (from != HostId) return;
            string reason = r.ReadStr();
            LastRefusal = reason;
            Plugin.Log.LogWarning($"Host refused us: {reason}");
            OnRefused?.Invoke(reason);
            if (Backend is TcpBackend tcp) tcp.Refused(reason);
            else Backend.Leave();
        }

        // ---- lifecycle -----------------------------------------------------

        private void OnPeerLost(ulong who)
        {
            if (IsHost && Peers.Remove(who))
            {
                Plugin.Log.LogInfo($"Player left: {NameOf(who)}");
                OnPlayerLeft?.Invoke(NameOf(who));
                var w = Begin(MsgType.PlayerLeft);
                w.W.Write(who);
                Broadcast(w, Channel.Control, reliable: true);
            }
            else if (IsHost && _joining.TryGetValue(who, out var was))
            {
                Plugin.Log.LogInfo($"{was} (#{who}) stopped joining");
                OnPlayerStoppedJoining?.Invoke(was);
                var w = Begin(MsgType.JoinNotice);
                w.WriteStr(was);
                w.W.Write((byte)1);
                Broadcast(w, Channel.Control, reliable: true);
            }
            _joining.Remove(who);
            Names.Remove(who);
            Keys.Remove(who);
            OnPeerDisconnected?.Invoke(who);
        }

        private void EndSession()
        {
            Peers.Clear();
            Names.Clear();
            Keys.Clear();
            Connected = false;
            _helloAt = -1f;
            _heardHostAt = -1f;
            _joining.Clear();
            OnSessionEnded?.Invoke();
        }

        /// <summary>Handle a message as if it had just come from this peer (a Request's inner message).</summary>
        public void Handle(ulong from, byte[] data, int length) => Dispatch(from, data, length);

        private void Dispatch(ulong from, byte[] data, int length)
        {
            PacketReader r;
            try { r = new PacketReader(data, length); }
            catch { return; }
            // Only the handshake (and fetching the world it offers) may come from someone not yet admitted.
            if (IsHost && r.Type != MsgType.Hello && r.Type != MsgType.NeedSave && !Peers.Contains(from)) return;
            if (!IsHost && from == HostId) { _helloAt = -1f; _heardHostAt = UnityEngine.Time.realtimeSinceStartup; } // anything from the host answers our Hello
            if (_handlers.TryGetValue(r.Type, out var h))
            {
                try { h(from, r); }
                catch (Exception e) { Plugin.Log.LogError($"Error handling {r.Type} from #{from}: {e}"); }
            }
            else Plugin.Log.LogWarning($"Unhandled message {r.Type} from #{from}");
        }

        private static string Sanitize(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length > 32) name = name.Substring(0, 32);
            return name.Length == 0 ? "Player" : name;
        }

        public void Update()
        {
            foreach (var b in _backends) b.Poll();
            // A Hello the host never answered (lost, or turned away by a race over Steam): send it again.
            if (IsClient && !Connected && _helloAt >= 0f && UnityEngine.Time.realtimeSinceStartup - _helloAt > HelloRetry)
            {
                Plugin.Log.LogInfo($"No answer to Hello in {HelloRetry:0} s: sending it again");
                SendHello();
            }
            if (IsClient && !Connected && _heardHostAt >= 0f && UnityEngine.Time.realtimeSinceStartup - _heardHostAt > JoinSilence)
            {
                string why = $"The host hasn't answered for {JoinSilence:0} s, so joining stopped.";
                Plugin.Log.LogWarning(why);
                _heardHostAt = -1f;
                OnJoinTimedOut?.Invoke(why);
                Backend.Leave();
            }
        }

        public void Dispose()
        {
            foreach (var b in _backends) b.Dispose();
            _backends.Clear();
            Instance = null;
        }
    }
}
