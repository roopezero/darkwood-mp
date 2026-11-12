using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;

namespace DarkwoodMP.Net
{
    /// <summary>
    /// Direct connections, as goasis does them: the host listens on a port over TCP for the game, and over UDP
    /// on the same port number for the server browser's info query. Works on any store build (Steam, GOG),
    /// so they can play together. Over the internet, forward the port (TCP and UDP) or use a VPN.
    ///
    /// Socket work happens on background threads, which only queue what they read; Poll, on the game's
    /// main thread, hands it on, so the game owns everything messages change (as goasis's wire.Conn).
    /// </summary>
    public sealed class TcpBackend : INetBackend
    {
        public const int DefaultPort = 27720;
        /// <summary>LAN search asks ports DefaultPort .. DefaultPort + LanPorts - 1.</summary>
        public const int LanPorts = 10;
        public const ulong HostPeerId = 1;
        private const ulong ClientIdBase = 1000;

        public string Name => "Direct";
        public string Status { get; private set; } = "Not connected";
        /// <summary>The info answer's "world" field (the browser's World column): the host's day and chapter, set by Plugin.</summary>
        public volatile string WorldLabel = "";
        public bool InSession { get; private set; }
        public bool IsHost { get; private set; }
        public ulong HostId => HostPeerId;
        public ulong LocalId => IsHost ? HostPeerId : 0; // a client's id comes in Welcome
        public string HostPassword { get; private set; } = "";
        public string JoinPassword { get; private set; } = "";
        public int MaxPlayers { get; private set; } = Protocol.MaxPlayers;
        /// <summary>Tests only (--dwmp-max-players N): more players than Protocol.MaxPlayers, for the five-game map4.</summary>
        public static int TestMaxPlayers;
        public int Port { get; private set; }
        public string ServerName { get; private set; } = "";
        /// <summary>Host: how many are playing, for the info query (set by the session).</summary>
        public Func<int> PlayerCount = () => 1;

        public event Action OnEntered;
        public event Action OnLeft;
        public event Action<ulong> OnPeerLost;
        public event Action<ulong, byte[], int> OnMessage;

        // Liveness and limits. A peer that has sent a Ping (every DarkwoodMP game does) is dropped after TimeoutMs
        // with nothing from it. Until a connection to the host has sent Hello, its messages are at most PreHelloMax
        // bytes, it has HelloTimeoutMs to send one, and at most MaxPending such connections are kept.
        private const int KeepaliveMs = 3000, TimeoutMs = 15000, HelloTimeoutMs = 10000;
        private const int PreHelloMax = 64 << 10, MaxPending = 8;
        // Past this much waiting to go out, the peer isn't taking anything: give up on it (as SteamTransport does).
        private const long MaxQueued = 64L << 20;
        private static readonly byte[] PingFrame = { 1, 0, 0, 0, (byte)MsgType.Ping };

        private sealed class Conn
        {
            public ulong Id;
            public TcpClient Client;
            public NetworkStream Stream;
            public volatile bool Closed;
            public string Remote;
            public bool Inbound;                  // accepted by the host (not our connection to a host)
            // Frames waiting for the writer thread; lock Out for these and for FlushThenClose.
            public readonly Queue<byte[]> Out = new Queue<byte[]>();
            public long OutBytes;
            public bool FlushThenClose;
            public volatile int LastHeard = Environment.TickCount;
            public readonly int ConnectedAt = Environment.TickCount;
            public volatile bool KeepsAlive;      // it has sent a Ping, so silence from it means it's gone
            public volatile bool SaidHello;
            public int Pending;                   // 1 while counted in _pending (Interlocked)
        }

        private enum EvKind { Connected, Message, Lost, ConnectFailed }
        private struct Ev { public EvKind Kind; public Conn Conn; public byte[] Data; public string Error; }

        private readonly ConcurrentQueue<Ev> _events = new ConcurrentQueue<Ev>();
        private readonly Dictionary<ulong, Conn> _conns = new Dictionary<ulong, Conn>();
        private TcpListener _listener;
        private UdpClient _info;
        private ulong _nextId = ClientIdBase;
        private int _generation; // ignores events from a session we already left
        private int _pending;    // inbound connections that haven't sent Hello yet
        private int _lastCheck;

        // ---- hosting -------------------------------------------------------

        public bool HostLost { get; private set; }

        public bool Host(string serverName, int port, string password, int maxPlayers)
        {
            if (InSession) return false;
            HostLost = false;
            try
            {
                _listener = new TcpListener(IPAddress.Any, port);
                // Hosting again soon after stopping: on Linux and macOS the port stays taken while the last
                // connections close (TIME_WAIT) unless this is set. Not on Windows, where it would let another
                // program take the same port.
                if (Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX)
                    _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listener.Start();
            }
            catch (Exception e)
            {
                Status = $"Couldn't listen on TCP port {port}: {e.Message}";
                _listener = null;
                return false;
            }
            try
            {
                _info = new UdpClient(new IPEndPoint(IPAddress.Any, port));
            }
            catch (Exception e)
            {
                // Still playable by address; only the browser can't see it.
                Plugin.Log.LogWarning($"Couldn't open UDP port {port} for the server browser: {e.Message}");
                _info = null;
            }

            int gen = ++_generation;
            Port = port;
            ServerName = string.IsNullOrWhiteSpace(serverName) ? "Darkwood" : serverName.Trim();
            HostPassword = password ?? "";
            MaxPlayers = Math.Max(2, Math.Min(TestMaxPlayers > 0 ? TestMaxPlayers : Protocol.MaxPlayers, maxPlayers));
            IsHost = true;
            InSession = true;
            Status = $"Hosting \"{ServerName}\" on port {port}";

            var listener = _listener;
            StartThread("DarkwoodMP accept", () => AcceptLoop(listener, gen));
            if (_info != null) { var udp = _info; StartThread("DarkwoodMP info", () => InfoLoop(udp, gen)); }
            Plugin.Log.LogInfo(Status);
            OnEntered?.Invoke();
            return true;
        }

        private void AcceptLoop(TcpListener listener, int gen)
        {
            while (gen == _generation)
            {
                TcpClient c;
                try { c = listener.AcceptTcpClient(); }
                catch { return; } // listener stopped
                // Anyone can open connections: only a few that haven't said Hello are kept at once.
                if (Interlocked.Increment(ref _pending) > MaxPending)
                {
                    Interlocked.Decrement(ref _pending);
                    try { c.Close(); } catch { }
                    continue;
                }
                c.NoDelay = true;
                var conn = new Conn { Client = c, Stream = c.GetStream(), Remote = c.Client.RemoteEndPoint?.ToString(), Inbound = true, Pending = 1 };
                lock (_conns) conn.Id = _nextId++;
                _events.Enqueue(new Ev { Kind = EvKind.Connected, Conn = conn });
                StartThread($"DarkwoodMP write #{conn.Id}", () => WriteLoop(conn, gen));
                StartThread($"DarkwoodMP read #{conn.Id}", () => ReadLoop(conn, gen));
            }
        }

        /// <summary>Answers the browser's "info" datagrams (goasis's A2S_INFO-like query).</summary>
        private void InfoLoop(UdpClient udp, int gen)
        {
            while (gen == _generation)
            {
                try
                {
                    var from = new IPEndPoint(IPAddress.Any, 0);
                    byte[] q = udp.Receive(ref from);
                    if (!ServerInfo.IsQuery(q, q.Length)) continue;
                    var info = new ServerInfo
                    {
                        Name = ServerName,
                        Players = PlayerCount(),
                        MaxPlayers = MaxPlayers,
                        Password = HostPassword.Length > 0,
                        LocalChars = false,
                        Version = PluginInfo.Version,
                    };
                    info.Fields["game"] = Protocol.Game;
                    info.Fields["proto"] = Protocol.Version.ToString();
                    if (!string.IsNullOrEmpty(WorldLabel)) info.Fields["world"] = WorldLabel;
                    byte[] a = info.EncodeAnswer();
                    udp.Send(a, a.Length, from);
                }
                catch (ObjectDisposedException) { return; }
                catch (SocketException) { if (gen != _generation) return; }
            }
        }

        // ---- joining -------------------------------------------------------

        private string _lastAddress, _lastPassword;

        public void Rejoin()
        {
            if (IsHost || _lastAddress == null) return;
            string a = _lastAddress, p = _lastPassword;
            Leave();
            Join(a, p);
        }

        public void Join(string address, string password)
        {
            if (InSession) return;
            _lastAddress = address; _lastPassword = password;
            if (!TryParseAddress(address, out string host, out int port))
            {
                Status = "Enter an address like 192.168.1.20 or example.com:27720";
                return;
            }
            int gen = ++_generation;
            IsHost = false;
            HostLost = false;
            InSession = true; // connecting; OnEntered once connected
            JoinPassword = password ?? "";
            Status = $"Connecting to {host}:{port}...";
            StartThread("DarkwoodMP connect", () =>
            {
                try
                {
                    var c = new TcpClient { NoDelay = true };
                    var ar = c.BeginConnect(host, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(8)))
                    {
                        c.Close();
                        throw new TimeoutException("no answer (check the address, and the host's firewall or port forwarding)");
                    }
                    c.EndConnect(ar);
                    var conn = new Conn { Id = HostPeerId, Client = c, Stream = c.GetStream(), Remote = $"{host}:{port}" };
                    if (gen != _generation) { c.Close(); return; }
                    _events.Enqueue(new Ev { Kind = EvKind.Connected, Conn = conn });
                    StartThread("DarkwoodMP write", () => WriteLoop(conn, gen));
                    ReadLoop(conn, gen);
                }
                catch (Exception e)
                {
                    if (gen == _generation) _events.Enqueue(new Ev { Kind = EvKind.ConnectFailed, Error = e.InnerException?.Message ?? e.Message });
                }
            });
        }

        public static bool TryParseAddress(string s, out string host, out int port)
        {
            host = null; port = DefaultPort;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            int colon = s.LastIndexOf(':');
            if (colon > 0 && s.IndexOf(':') == colon)
            {
                if (!int.TryParse(s.Substring(colon + 1), out port) || port <= 0 || port > 65535) return false;
                s = s.Substring(0, colon);
            }
            host = s;
            return host.Length > 0;
        }

        /// <summary>"host:port", with the default port filled in, as the browser's lists keep addresses.</summary>
        public static string Normalize(string address) =>
            TryParseAddress(address, out var h, out var p) ? $"{h}:{p}" : null;

        // ---- reading and writing -------------------------------------------

        private void ReadLoop(Conn conn, int gen)
        {
            var head = new byte[4];
            try
            {
                while (gen == _generation && !conn.Closed)
                {
                    ReadFull(conn.Stream, head, 4);
                    int len = (int)BitConverter.ToUInt32(head, 0); // little-endian on every platform Darkwood runs on
                    int max = conn.Inbound && !conn.SaidHello ? PreHelloMax : Protocol.MaxMessage;
                    if (len < 1 || len > max) throw new IOException($"bad message length {len}");
                    var buf = new byte[len];
                    ReadFull(conn.Stream, buf, len);
                    conn.LastHeard = Environment.TickCount;
                    if (len == 1 && buf[0] == (byte)MsgType.Ping) { conn.KeepsAlive = true; continue; }
                    if (buf[0] == (byte)MsgType.Hello && !conn.SaidHello) { conn.SaidHello = true; Unpend(conn); }
                    _events.Enqueue(new Ev { Kind = EvKind.Message, Conn = conn, Data = buf });
                }
            }
            catch (Exception e)
            {
                if (gen == _generation && !conn.Closed)
                    _events.Enqueue(new Ev { Kind = EvKind.Lost, Conn = conn, Error = e.Message });
            }
            finally { Unpend(conn); }
        }

        private void Unpend(Conn c)
        {
            if (Interlocked.Exchange(ref c.Pending, 0) == 1) Interlocked.Decrement(ref _pending);
        }

        /// <summary>
        /// Sends what Write queued, so the game's main thread never waits on the network (a joiner downloading the
        /// world over a slow link used to stall the host's frames). A Ping goes first and whenever it's been quiet.
        /// </summary>
        private void WriteLoop(Conn c, int gen)
        {
            try
            {
                c.Stream.WriteTimeout = 30000;
                c.Stream.Write(PingFrame, 0, PingFrame.Length);
                while (true)
                {
                    byte[] f = null;
                    lock (c.Out)
                    {
                        if (c.Out.Count == 0 && !c.Closed) Monitor.Wait(c.Out, KeepaliveMs);
                        if (c.Out.Count > 0 && (!c.Closed || c.FlushThenClose)) { f = c.Out.Dequeue(); c.OutBytes -= f.Length; }
                        else if (c.Closed) break;
                    }
                    f = f ?? PingFrame; // nothing to send for a while
                    c.Stream.Write(f, 0, f.Length);
                }
                // Let what was written (e.g. Refused, or a leaving client's character) go out, then close.
                if (c.FlushThenClose) c.Client.Client.Shutdown(SocketShutdown.Send);
            }
            catch (Exception e)
            {
                if (gen == _generation && !c.Closed) _events.Enqueue(new Ev { Kind = EvKind.Lost, Conn = c, Error = e.Message });
            }
            finally { try { c.Client.Close(); } catch { } }
        }

        private static void ReadFull(Stream s, byte[] buf, int n)
        {
            int got = 0;
            while (got < n)
            {
                int k = s.Read(buf, got, n - got);
                if (k <= 0) throw new EndOfStreamException("connection closed");
                got += k;
            }
        }

        public bool Send(ulong to, byte[] data, int length, int channel, bool reliable)
        {
            Conn c;
            lock (_conns) if (!_conns.TryGetValue(to, out c)) return false;
            return Write(c, data, length);
        }

        private bool Write(Conn c, byte[] data, int length)
        {
            if (c.Closed) return false;
            var f = new byte[4 + length];
            BitConverter.GetBytes((uint)length).CopyTo(f, 0);
            Buffer.BlockCopy(data, 0, f, 4, length);
            lock (c.Out)
            {
                if (c.Closed) return false;
                if (c.OutBytes + f.Length <= MaxQueued)
                {
                    c.Out.Enqueue(f);
                    c.OutBytes += f.Length;
                    Monitor.Pulse(c.Out);
                    return true;
                }
            }
            _events.Enqueue(new Ev { Kind = EvKind.Lost, Conn = c, Error = $"it isn't taking what we send ({MaxQueued >> 20} MiB waiting)" });
            return false;
        }

        public void Kick(ulong peer)
        {
            Conn c;
            lock (_conns) if (!_conns.TryGetValue(peer, out c)) return;
            Drop(c, notify: false, flush: true); // Refused, if sent, goes out first
        }

        // ---- main thread ---------------------------------------------------

        public void Poll()
        {
            if (InSession && Environment.TickCount - _lastCheck > 1000) { _lastCheck = Environment.TickCount; CheckAlive(); }
            while (_events.TryDequeue(out var ev))
            {
                switch (ev.Kind)
                {
                    case EvKind.Connected:
                        lock (_conns) _conns[ev.Conn.Id] = ev.Conn;
                        if (IsHost) Plugin.Log.LogInfo($"Connection from {ev.Conn.Remote} as #{ev.Conn.Id}");
                        else
                        {
                            Status = $"Connected to {ev.Conn.Remote}";
                            OnEntered?.Invoke();
                        }
                        break;
                    case EvKind.Message:
                        if (!ev.Conn.Closed) OnMessage?.Invoke(ev.Conn.Id, ev.Data, ev.Data.Length);
                        break;
                    case EvKind.Lost:
                        if (ev.Conn.Closed) break;
                        if (IsHost)
                        {
                            Plugin.Log.LogInfo($"#{ev.Conn.Id} disconnected: {ev.Error}");
                            Drop(ev.Conn, notify: true, flush: false);
                        }
                        else { HostLost = true; Shutdown($"Disconnected from host: {ev.Error}"); }
                        break;
                    case EvKind.ConnectFailed:
                        Shutdown($"Couldn't connect: {ev.Error}");
                        break;
                }
            }
        }

        /// <summary>Connections gone quiet (no Ping for TimeoutMs), and ones that never said Hello.</summary>
        private void CheckAlive()
        {
            List<Conn> all;
            lock (_conns) all = new List<Conn>(_conns.Values);
            int now = Environment.TickCount;
            foreach (var c in all)
            {
                if (c.Closed) continue;
                if (c.KeepsAlive && now - c.LastHeard > TimeoutMs)
                    _events.Enqueue(new Ev { Kind = EvKind.Lost, Conn = c, Error = $"nothing from it for {TimeoutMs / 1000} s" });
                else if (c.Inbound && !c.SaidHello && now - c.ConnectedAt > HelloTimeoutMs)
                {
                    Plugin.Log.LogInfo($"#{c.Id} ({c.Remote}) didn't say Hello in {HelloTimeoutMs / 1000} s: closed");
                    Drop(c, notify: false, flush: false);
                }
            }
        }

        // flush: let what's queued go out before closing; otherwise close at once (which also ends a stuck write).
        private void Drop(Conn c, bool notify, bool flush)
        {
            if (c.Closed) return;
            Close(c, flush);
            lock (_conns) _conns.Remove(c.Id);
            if (notify) OnPeerLost?.Invoke(c.Id);
        }

        private static void Close(Conn c, bool flush)
        {
            lock (c.Out)
            {
                c.Closed = true;
                c.FlushThenClose = flush;
                Monitor.Pulse(c.Out);
            }
            if (!flush) try { c.Client.Close(); } catch { }
        }

        /// <summary>Client: show why the host turned us down, and end the session.</summary>
        public void Refused(string reason) => Shutdown($"Refused: {reason}");

        public void Leave()
        {
            World.Characters.Instance?.SaveNow(); // our character goes to the host before we go
            Shutdown("Not connected");
        }

        private void Shutdown(string status)
        {
            _generation++;
            bool was = InSession;
            InSession = false;
            IsHost = false;
            List<Conn> all;
            lock (_conns) { all = new List<Conn>(_conns.Values); _conns.Clear(); }
            foreach (var c in all) Close(c, flush: true); // a leaving client's character, the host's HostAway
            try { _listener?.Stop(); } catch { }
            try { _info?.Close(); } catch { }
            _listener = null; _info = null;
            while (_events.TryDequeue(out _)) { }
            Status = status;
            if (was) OnLeft?.Invoke();
        }

        private static void StartThread(string name, ThreadStart body) =>
            new Thread(body) { IsBackground = true, Name = name }.Start();

        /// <summary>This machine's IPv4 addresses (LAN, VPN), for the host to give out.</summary>
        public static List<string> LocalAddresses()
        {
            var list = new List<string>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var a in nic.GetIPProperties().UnicastAddresses)
                        if (a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
                            list.Add(a.Address.ToString());
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't list network interfaces: {e.Message}"); }
            return list;
        }

        public void Dispose() => Leave();
    }
}
