using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace DarkwoodMP.Net
{
    /// <summary>
    /// The server browser, as goasis's package browser: Favorites (servers the player added), History (servers
    /// joined, with when) and LAN (found by broadcasting the info query). Each server is asked about itself with
    /// the UDP info query, which also gives its ping.
    ///
    /// The lists are kept in BepInEx/config/DarkwoodMP.servers.ini, in goasis's format:
    ///   [FAVORITES]
    ///   example.com:27720=
    ///   [HISTORY]
    ///   192.168.1.5:27720=1761400000
    /// </summary>
    public sealed class ServerBrowser
    {
        public enum Tab { Favorites, History, Lan }

        public sealed class Entry
        {
            public string Addr;
            public ServerInfo Info;  // null until it answers
            public bool Querying;
            public bool Failed;      // didn't answer in time
            public int PingMs;
        }

        public readonly List<string> Favorites = new List<string>();
        public readonly Dictionary<string, long> History = new Dictionary<string, long>();
        /// <summary>What we know about each address, shared by the tabs.</summary>
        public readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>();
        public readonly List<string> Lan = new List<string>();
        public bool SearchingLan { get; private set; }

        private readonly string _path;
        private readonly ConcurrentQueue<Action> _done = new ConcurrentQueue<Action>();
        private const int QueryTimeoutMs = 1500;

        public ServerBrowser(string configDir)
        {
            _path = Path.Combine(configDir, "DarkwoodMP.servers.ini");
            Load();
        }

        public List<string> List(Tab tab)
        {
            switch (tab)
            {
                case Tab.Favorites: return Favorites;
                case Tab.History: return History.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
                default: return Lan;
            }
        }

        public Entry Get(string addr)
        {
            if (!Entries.TryGetValue(addr, out var e)) Entries[addr] = e = new Entry { Addr = addr };
            return e;
        }

        /// <summary>Call every frame: applies answers that came in on background threads.</summary>
        public void Update()
        {
            while (_done.TryDequeue(out var a)) a();
        }

        // ---- lists ---------------------------------------------------------

        public void AddFavorite(string addr)
        {
            addr = TcpBackend.Normalize(addr);
            if (addr == null || Favorites.Contains(addr)) return;
            Favorites.Add(addr);
            Save();
            Query(addr);
        }

        public void RemoveFavorite(string addr) { if (Favorites.Remove(addr)) Save(); }

        public void Joined(string addr)
        {
            addr = TcpBackend.Normalize(addr);
            if (addr == null) return;
            History[addr] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            Save();
        }

        public void RemoveHistory(string addr) { if (History.Remove(addr)) Save(); }

        private void Load()
        {
            try
            {
                if (!File.Exists(_path)) return;
                string section = "";
                foreach (var raw in File.ReadAllLines(_path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith(";")) continue;
                    if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2).ToUpperInvariant(); continue; }
                    int eq = line.IndexOf('=');
                    string key = (eq >= 0 ? line.Substring(0, eq) : line).Trim();
                    string val = eq >= 0 ? line.Substring(eq + 1).Trim() : "";
                    if (key.Length == 0) continue;
                    if (section == "FAVORITES" && !Favorites.Contains(key)) Favorites.Add(key);
                    else if (section == "HISTORY") { long.TryParse(val, out long t); History[key] = t; }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't read {_path}: {e.Message}"); }
        }

        private void Save()
        {
            try
            {
                var b = new StringBuilder();
                b.Append("[FAVORITES]\r\n");
                foreach (var a in Favorites) b.Append(a).Append("=\r\n");
                b.Append("[HISTORY]\r\n");
                foreach (var kv in History) b.Append(kv.Key).Append('=').Append(kv.Value).Append("\r\n");
                File.WriteAllText(_path, b.ToString());
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't write {_path}: {e.Message}"); }
        }

        // ---- queries -------------------------------------------------------

        public void Refresh(Tab tab)
        {
            if (tab == Tab.Lan) { SearchLan(); return; }
            foreach (var a in List(tab)) Query(a);
        }

        /// <summary>Ask one server about itself (UDP info query to the same port number as its game).</summary>
        public void Query(string addr)
        {
            var e = Get(addr);
            if (e.Querying) return;
            e.Querying = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                ServerInfo info = null; int ping = 0;
                try
                {
                    if (TcpBackend.TryParseAddress(addr, out string host, out int port))
                    {
                        var ip = Dns.GetHostAddresses(host).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                        if (ip != null)
                        {
                            using (var udp = new UdpClient(AddressFamily.InterNetwork))
                            {
                                udp.Client.ReceiveTimeout = QueryTimeoutMs;
                                var sw = Stopwatch.StartNew();
                                udp.Send(ServerInfo.Query, ServerInfo.Query.Length, new IPEndPoint(ip, port));
                                var from = new IPEndPoint(IPAddress.Any, 0);
                                byte[] a = udp.Receive(ref from);
                                ping = (int)sw.ElapsedMilliseconds;
                                info = ServerInfo.Parse(a, a.Length);
                            }
                        }
                    }
                }
                catch { /* no answer */ }
                _done.Enqueue(() =>
                {
                    e.Querying = false;
                    e.Info = info;
                    e.Failed = info == null;
                    e.PingMs = ping;
                });
            });
        }

        /// <summary>
        /// Where the LAN search asks. A broadcast to 255.255.255.255 leaves Windows by one network adapter only, and
        /// Windows' firewall often drops the answers to a broadcast (they come from an address it was never sent to).
        /// So the query also goes to each adapter's own subnet broadcast, and, on the default port, to every
        /// address in the /24 around each of our addresses: answers to those are let through.
        /// </summary>
        private static List<IPEndPoint> LanTargets()
        {
            var list = new List<IPEndPoint>();
            var sweep = new List<IPEndPoint>(); // after every broadcast: it's the slow part
            void Ports(IPAddress a) { for (int p = TcpBackend.DefaultPort; p < TcpBackend.DefaultPort + TcpBackend.LanPorts; p++) list.Add(new IPEndPoint(a, p)); }
            Ports(IPAddress.Broadcast);
            try
            {
                foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                    foreach (var u in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (u.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        byte[] ip = u.Address.GetAddressBytes();
                        byte[] mask = u.IPv4Mask?.GetAddressBytes();
                        if (mask != null && mask.Length == 4)
                        {
                            var bc = new byte[4];
                            for (int i = 0; i < 4; i++) bc[i] = (byte)(ip[i] | ~mask[i]);
                            Ports(new IPAddress(bc));
                        }
                        for (int last = 1; last < 255; last++)
                        {
                            if (last == ip[3]) continue;
                            sweep.Add(new IPEndPoint(new IPAddress(new[] { ip[0], ip[1], ip[2], (byte)last }), TcpBackend.DefaultPort));
                        }
                    }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Couldn't list the network adapters, searching by broadcast only: {e.Message}"); }
            list.AddRange(sweep);
            return list;
        }

        /// <summary>Ask the LAN who's hosting (see LanTargets), and list who answers.</summary>
        public void SearchLan()
        {
            if (SearchingLan) return;
            SearchingLan = true;
            Lan.Clear();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    using (var udp = new UdpClient(AddressFamily.InterNetwork))
                    {
                        udp.EnableBroadcast = true;
                        var sw = Stopwatch.StartNew();
                        var targets = LanTargets();
                        // The queries go out on their own thread while this one takes the answers. Sending to every
                        // address of each adapter's /24 is slow (each address with no machine behind it waits on
                        // ARP: 800 addresses took 9 s on Linux), and the broadcasts, sent first, are answered in
                        // milliseconds. The wait ends QueryTimeoutMs after the last query went.
                        long sentAt = -1; int failed = 0;
                        var sender = new Thread(() =>
                        {
                            foreach (var to in targets)
                            {
                                try { udp.Send(ServerInfo.Query, ServerInfo.Query.Length, to); }
                                catch (SocketException) { failed++; } // an address this adapter can't reach
                                catch (ObjectDisposedException) { return; }
                            }
                            Interlocked.Exchange(ref sentAt, sw.ElapsedMilliseconds);
                        }) { IsBackground = true, Name = "DarkwoodMP LAN search" };
                        sender.Start();
                        while (true)
                        {
                            long done = Interlocked.Read(ref sentAt);
                            if (done >= 0 && sw.ElapsedMilliseconds >= done + QueryTimeoutMs) break;
                            if (sw.ElapsedMilliseconds > 30000) break; // the sends never finished
                            udp.Client.ReceiveTimeout = done >= 0 ? (int)Math.Max(1, done + QueryTimeoutMs - sw.ElapsedMilliseconds) : 200;
                            var from = new IPEndPoint(IPAddress.Any, 0);
                            byte[] a;
                            try { a = udp.Receive(ref from); }
                            // A machine that isn't hosting answers with ICMP "port unreachable", which the next Receive
                            // reports: ConnectionReset on Windows, ConnectionRefused on Linux. That's one address
                            // saying no: keep listening. Only the timeout ends the wait.
                            catch (SocketException se) when (se.SocketErrorCode != SocketError.TimedOut && se.SocketErrorCode != SocketError.WouldBlock) { continue; }
                            catch (SocketException) { continue; } // timed out: the deadline above decides

                            var info = ServerInfo.Parse(a, a.Length);
                            if (info == null) continue;
                            string addr = $"{from.Address}:{from.Port}";
                            int ping = (int)sw.ElapsedMilliseconds;
                            _done.Enqueue(() =>
                            {
                                var e = Get(addr);
                                e.Info = info; e.Failed = false; e.PingMs = ping;
                                if (!Lan.Contains(addr)) Lan.Add(addr);
                            });
                        }
                        Plugin.Log.LogInfo($"LAN search: asked {targets.Count} addresses ({failed} couldn't be sent), the last at {Interlocked.Read(ref sentAt)} ms");
                    }
                }
                catch (Exception ex) { Plugin.Log.LogWarning($"LAN search failed: {ex.Message}"); }
                _done.Enqueue(() => SearchingLan = false);
            });
        }
    }
}
