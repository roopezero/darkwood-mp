using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Xml;

namespace DarkwoodMP.Net
{
    /// <summary>
    /// Asks the host's router to forward the Direct port (TCP for the game, UDP for the server browser) to this
    /// computer, so a host doesn't have to set up port forwarding by hand. Tries UPnP IGD first (SSDP discovery,
    /// then SOAP AddPortMapping), then NAT-PMP (RFC 6886) on the default gateway. Mappings are leased for an hour
    /// and renewed every half hour while hosting, and removed when hosting stops.
    ///
    /// All of it runs on a background thread; the panel only reads <see cref="Status"/> and <see cref="ExternalIp"/>.
    /// Only the host's own router is ever contacted. It can't tell whether the host is reachable from the internet
    /// (that needs someone outside), only whether the router agreed. Behind carrier-grade NAT or a second router,
    /// the router's own "external" address is a private one: that's reported, since forwarding can't help there.
    /// </summary>
    public sealed class PortMapper
    {
        /// <summary>
        /// Tests only ("--dwmp-upnp-test HOST:PORT"): the router is a script there. Its device description is at
        /// http://HOST:PORT/rootDesc.xml and it answers NAT-PMP on UDP HOST:PORT. No SSDP and no real gateway.
        /// </summary>
        public static string TestRouter;

        private const int LeaseSeconds = 3600, RenewMs = 1800 * 1000;
        private const string Description = "DarkwoodMP";

        public volatile string Status = "";
        public volatile string ExternalIp;
        /// <summary>The router's external address is private or shared (CGNAT, a second router): forwarding won't reach us.</summary>
        public volatile bool BehindAnotherNat;
        /// <summary>The TCP port the router forwards to us (NAT-PMP may give another than asked), or 0.</summary>
        public int ExternalPort { get
            {
                lock (_lock)
                {
                    return _mapped?.ExternalTcp ?? 0;
                }
            }
        }
        public bool Mapped { get
            {
                lock (_lock)
                {
                    return _mapped != null;
                }
            }
        }

        private readonly object _lock = new object();
        private int _generation;
        private Mapping _mapped; // what's open now, to renew and remove (under _lock)
        private volatile string _refused; // why the router said no, when it said why

        private sealed class Mapping
        {
            public bool Upnp;
            public string ControlUrl, ServiceType; // UPnP
            public IPEndPoint Gateway;             // NAT-PMP
            public string InternalIp;
            public int Port, ExternalTcp, ExternalUdp;
            public bool Permanent;                 // the router only takes leases of 0 (no expiry)
        }

        /// <summary>Starts opening <paramref name="port"/> in the background (stops any earlier one first).</summary>
        public void Start(int port)
        {
            Stop();
            int gen;
            lock (_lock)
            {
                gen = ++_generation;
            }
            Status = "Asking your router to open the port (UPnP)...";
            ExternalIp = null; BehindAnotherNat = false; _refused = null;
            var t = new Thread(() => Run(port, gen)) { IsBackground = true, Name = "DarkwoodMP port mapping" };
            t.Start();
        }

        /// <summary>
        /// Removes the mappings. In the background, unless <paramref name="wait"/> (at quit), where it waits at most
        /// about a second and a half: the process is about to end.
        /// </summary>
        public void Stop(bool wait = false)
        {
            Mapping m;
            lock (_lock) { _generation++; m = _mapped; _mapped = null; }
            Status = "";
            if (m == null)
            {
                return;
            }
            var t = new Thread(() => Remove(m)) { IsBackground = true, Name = "DarkwoodMP port unmapping" };
            t.Start();
            if (wait)
            {
                t.Join(1500);
            }
        }

        private bool Current(int gen)
        {
            lock (_lock)
            {
                return gen == _generation;
            }
        }

        private void Run(int port, int gen)
        {
            try
            {
                Mapping m = TryUpnp(port, gen);
                if (m == null && Current(gen))
                {
                    Status = "No UPnP answer; trying NAT-PMP...";
                    m = TryNatPmp(port);
                }
                if (!Current(gen))
                {
                    if (m != null)
                    {
                        Remove(m);
                    }
                    return;
                }
                if (m == null)
                {
                    Status = _refused ?? "Your router didn't open the port (UPnP and NAT-PMP are off or missing). Forward it by hand, or use a VPN.";
                    Plugin.Log.LogInfo("[port mapping] " + Status);
                    return;
                }
                lock (_lock)
                {
                    if (gen != _generation) { m = null; }
                    else
                    {
                        _mapped = m;
                    }
                }
                if (m == null)
                {
                    return;
                }
                Report(m);
                // Renew before the lease runs out, for as long as we're hosting.
                while (!m.Permanent)
                {
                    for (int waited = 0; waited < RenewMs; waited += 1000)
                    {
                        Thread.Sleep(1000);
                        if (!Current(gen))
                        {
                            return;
                        }
                    }
                    bool ok = m.Upnp ? AddUpnp(m, "TCP", LeaseSeconds, out _) : PmpMap(m.Gateway, 2, m.Port, m.ExternalTcp, LeaseSeconds) > 0;
                    if (ok && m.ExternalUdp > 0)
                    {
                        ok = m.Upnp ? AddUpnp(m, "UDP", LeaseSeconds, out _) : PmpMap(m.Gateway, 1, m.Port, m.ExternalUdp, LeaseSeconds) > 0;
                    }
                    if (!Current(gen))
                    {
                        return;
                    }
                    Plugin.Log.LogInfo(ok ? "[port mapping] renewed" : "[port mapping] the router didn't renew the mapping");
                    if (!ok)
                    {
                        Status = "Your router stopped forwarding the port. Forward it by hand, or use a VPN.";
                    }
                }
            }
            catch (Exception e)
            {
                if (Current(gen))
                {
                    Status = "Couldn't open the port on your router: " + e.Message;
                }
                Plugin.Log.LogWarning("[port mapping] " + e);
            }
        }

        private void Report(Mapping m)
        {
            string how = m.Upnp ? "UPnP" : "NAT-PMP";
            string ip = ExternalIp;
            BehindAnotherNat = ip != null && IPAddress.TryParse(ip, out IPAddress a) && IsPrivate(a);
            string ports = m.ExternalTcp == m.ExternalUdp ? $"port {m.ExternalTcp} (TCP and UDP)"
                : m.ExternalUdp <= 0 ? $"TCP port {m.ExternalTcp} (not UDP)" : $"TCP {m.ExternalTcp}, UDP {m.ExternalUdp}";
            if (BehindAnotherNat)
            {
                Status = $"Your router opened {ports} ({how}), but its internet address {ip} is a shared or private one (your provider's NAT, or a second router): friends over the internet probably still can't reach you. Use Steam or a VPN.";
            }
            else
            {
                Status = $"Your router opened {ports} ({how}).";
            }
            if (m.ExternalUdp != m.ExternalTcp)
            {
                Status += " Friends' server browsers can't see you over the internet: they connect by address.";
            }
            Plugin.Log.LogInfo($"[port mapping] {how}: {m.InternalIp}:{m.Port} -> {ports}, external address {ip ?? "unknown"}{(m.Permanent ? ", permanent" : $", lease {LeaseSeconds} s")}{(BehindAnotherNat ? " (private: another NAT in front)" : "")}");
        }

        private void Remove(Mapping m)
        {
            try
            {
                if (m.Upnp)
                {
                    DeleteUpnp(m, "TCP", m.ExternalTcp);
                    DeleteUpnp(m, "UDP", m.ExternalUdp);
                }
                else
                {
                    PmpMap(m.Gateway, 2, m.Port, 0, 0, tries: 2);
                    PmpMap(m.Gateway, 1, m.Port, 0, 0, tries: 2);
                }
                Plugin.Log.LogInfo($"[port mapping] removed {(m.Upnp ? "UPnP" : "NAT-PMP")} mapping of port {m.Port}");
            }
            catch (Exception e) { Plugin.Log.LogWarning("[port mapping] couldn't remove the mapping: " + e.Message); }
        }

        /// <summary>100.64/10 (carrier-grade NAT), the RFC 1918 ranges, link-local, loopback and 0.0.0.0.</summary>
        public static bool IsPrivate(IPAddress a)
        {
            if (a.AddressFamily != AddressFamily.InterNetwork)
            {
                return false;
            }
            byte[] b = a.GetAddressBytes();
            return b[0] == 10 || b[0] == 127 || b[0] == 0
                || (b[0] == 100 && (b[1] & 0xC0) == 64)
                || (b[0] == 172 && (b[1] & 0xF0) == 16)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254);
        }

        // ---- UPnP IGD ------------------------------------------------------

        private static readonly string[] SearchTargets =
        {
            "urn:schemas-upnp-org:device:InternetGatewayDevice:1",
            "urn:schemas-upnp-org:device:InternetGatewayDevice:2",
            "urn:schemas-upnp-org:service:WANIPConnection:1",
            "urn:schemas-upnp-org:service:WANPPPConnection:1",
        };

        private Mapping TryUpnp(int port, int gen)
        {
            var locations = TestRouter != null ? new List<string> { $"http://{TestRouter}/rootDesc.xml" } : Discover();
            foreach (string loc in locations)
            {
                if (!Current(gen))
                {
                    return null;
                }
                try
                {
                    if (!FindService(loc, out string control, out string type))
                    {
                        continue;
                    }
                    var uri = new Uri(control);
                    var m = new Mapping { Upnp = true, ControlUrl = control, ServiceType = type, Port = port, ExternalTcp = port, ExternalUdp = port };
                    m.InternalIp = LocalAddressToward(uri.Host, uri.Port);
                    if (m.InternalIp == null)
                    {
                        continue;
                    }
                    try
                    {
                        if (Soap(m, "GetExternalIPAddress", "", out _) is Dictionary<string, string> f && f.TryGetValue("NewExternalIPAddress", out string ext) && ext.Length > 0)
                        {
                            ExternalIp = ext;
                        }
                    }
                    catch (Exception e) { Plugin.Log.LogInfo($"[port mapping] GetExternalIPAddress at {control}: {e.Message}"); }
                    if (!AddUpnp(m, "TCP", LeaseSeconds, out string why))
                    {
                        Plugin.Log.LogInfo($"[port mapping] UPnP router at {loc} refused TCP {port}: {why}");
                        if (why.StartsWith("718"))
                        {
                            _refused = $"Port {port} is already forwarded to another computer on your router. Pick another port, or forward it by hand.";
                        }
                        continue;
                    }
                    if (!AddUpnp(m, "UDP", m.Permanent ? 0 : LeaseSeconds, out why))
                    {
                        // The game itself still works over TCP; only the browser's info query is missing.
                        Plugin.Log.LogInfo($"[port mapping] UPnP router refused UDP {port}: {why}");
                        m.ExternalUdp = 0;
                    }
                    return m;
                }
                catch (Exception e) { Plugin.Log.LogInfo($"[port mapping] UPnP device at {loc}: {e.Message}"); }
            }
            return null;
        }

        /// <summary>
        /// SSDP M-SEARCH from every local IPv4 address (a VPN may hold the default route); the LOCATIONs that answer.
        /// Each search goes to the multicast group and also straight to each default gateway's port 1900. A stateful
        /// firewall (firewalld's default zone, for one) drops the router's answers to a multicast search, since they
        /// come from the router's address and not the group's; answers to a search sent to the router itself get through.
        /// </summary>
        private static List<string> Discover()
        {
            var found = new List<string>();
            var socks = new List<UdpClient>();
            var targets = new List<IPEndPoint> { new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900) };
            foreach (IPAddress gw in GatewayAddresses())
            {
                targets.Add(new IPEndPoint(gw, 1900));
            }
            try
            {
                foreach (string ip in TcpBackend.LocalAddresses())
                {
                    UdpClient u;
                    try
                    {
                        u = new UdpClient(new IPEndPoint(IPAddress.Parse(ip), 0));
                        u.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 2);
                        socks.Add(u);
                    }
                    catch (Exception e) { Plugin.Log.LogInfo($"[port mapping] SSDP from {ip}: {e.Message}"); continue; }
                    foreach (IPEndPoint to in targets)
                    {
                        try
                        {
                            foreach (string st in SearchTargets)
                            {
                                byte[] q = Encoding.ASCII.GetBytes($"M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\nST: {st}\r\n\r\n");
                                u.Send(q, q.Length, to);
                            }
                        }
                        catch (Exception e) { Plugin.Log.LogInfo($"[port mapping] SSDP from {ip} to {to}: {e.Message}"); }
                    }
                }
                int until = Environment.TickCount + 2500;
                while (until - Environment.TickCount > 0 && socks.Count > 0)
                {
                    var ready = new List<Socket>();
                    foreach (var u in socks)
                    {
                        ready.Add(u.Client);
                    }
                    Socket.Select(ready, null, null, Math.Max(1, until - Environment.TickCount) * 1000);
                    foreach (var s in ready)
                    {
                        var from = new IPEndPoint(IPAddress.Any, 0);
                        EndPoint ep = from;
                        var buf = new byte[2048];
                        int n;
                        try { n = s.ReceiveFrom(buf, ref ep); } catch { continue; }
                        foreach (string line in Encoding.ASCII.GetString(buf, 0, n).Split('\n'))
                        {
                            int colon = line.IndexOf(':');
                            if (colon <= 0 || !line.Substring(0, colon).Trim().Equals("LOCATION", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                            string loc = line.Substring(colon + 1).Trim();
                            if (loc.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !found.Contains(loc))
                            {
                                found.Add(loc);
                            }
                        }
                    }
                    // A router has answered: give others a moment, but don't wait out the whole MX.
                    if (found.Count > 0 && until - Environment.TickCount > 500)
                    {
                        until = Environment.TickCount + 500;
                    }
                }
            }
            finally
            {
                foreach (var u in socks)
                {
                    try
                    { u.Close(); }
                    catch { }
                }
            }
            Plugin.Log.LogInfo($"[port mapping] SSDP: {found.Count} answer(s){(found.Count > 0 ? ": " + string.Join(", ", found) : "")}");
            return found;
        }

        /// <summary>The first WANIPConnection or WANPPPConnection service in a device description, and its control URL.</summary>
        private static bool FindService(string location, out string control, out string type)
        {
            control = type = null;
            var doc = new XmlDocument { XmlResolver = null };
            string xml = Http("GET", location, null, null, out int status);
            if (status != 200)
            {
                return false;
            }
            doc.LoadXml(xml);
            string baseUrl = location;
            foreach (XmlNode n in doc.GetElementsByTagName("URLBase"))
            {
                if (!string.IsNullOrWhiteSpace(n.InnerText))
                {
                    baseUrl = n.InnerText.Trim();
                }
            }
            // WANIPConnection first: a router with both usually forwards through it.
            foreach (string want in new[] { "WANIPConnection", "WANPPPConnection" })
            {
                foreach (XmlNode s in doc.GetElementsByTagName("service"))
                {
                    string st = Child(s, "serviceType"), cu = Child(s, "controlURL");
                    if (st == null || cu == null || st.IndexOf(want, StringComparison.Ordinal) < 0)
                    {
                        continue;
                    }
                    type = st;
                    control = new Uri(new Uri(baseUrl), cu).ToString();
                    return true;
                }
            }
            return false;
        }

        private static string Child(XmlNode n, string name)
        {
            foreach (XmlNode c in n.ChildNodes)
            {
                if (c.LocalName == name)
                {
                    return c.InnerText.Trim();
                }
            }
            return null;
        }

        private bool AddUpnp(Mapping m, string proto, int lease, out string why)
        {
            int ext = proto == "TCP" ? m.ExternalTcp : m.ExternalUdp;
            string args(int l) =>
                $"<NewRemoteHost></NewRemoteHost><NewExternalPort>{ext}</NewExternalPort><NewProtocol>{proto}</NewProtocol>" +
                $"<NewInternalPort>{m.Port}</NewInternalPort><NewInternalClient>{m.InternalIp}</NewInternalClient><NewEnabled>1</NewEnabled>" +
                $"<NewPortMappingDescription>{Description}</NewPortMappingDescription><NewLeaseDuration>{l}</NewLeaseDuration>";
            if (Soap(m, "AddPortMapping", args(m.Permanent ? 0 : lease), out why) != null)
            {
                return true;
            }
            // 725 OnlyPermanentLeasesSupported: some routers take only 0, which never expires (removed when we stop).
            if (why.StartsWith("725") && !m.Permanent && Soap(m, "AddPortMapping", args(0), out why) != null) { m.Permanent = true; return true; }
            return false;
        }

        private static void DeleteUpnp(Mapping m, string proto, int ext)
        {
            if (ext <= 0)
            {
                return;
            }
            Soap(m, "DeletePortMapping", $"<NewRemoteHost></NewRemoteHost><NewExternalPort>{ext}</NewExternalPort><NewProtocol>{proto}</NewProtocol>", out _);
        }

        /// <summary>The answer's fields by name; null and <paramref name="error"/> ("CODE description") when the router refused.</summary>
        private static Dictionary<string, string> Soap(Mapping m, string action, string args, out string error)
        {
            string body = "<?xml version=\"1.0\"?>\r\n<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                $"<s:Body><u:{action} xmlns:u=\"{m.ServiceType}\">{args}</u:{action}></s:Body></s:Envelope>";
            string text = Http("POST", m.ControlUrl, body, $"\"{m.ServiceType}#{action}\"", out int status);
            var doc = new XmlDocument { XmlResolver = null };
            try { doc.LoadXml(text); } catch { doc = null; }
            if (status == 200 && doc != null)
            {
                error = null;
                var fields = new Dictionary<string, string>();
                foreach (XmlNode n in doc.GetElementsByTagName("*"))
                {
                    if (!n.HasChildNodes || (n.ChildNodes.Count == 1 && n.FirstChild is XmlText))
                    {
                        fields[n.LocalName] = n.InnerText.Trim();
                    }
                }
                return fields;
            }
            string code = null, desc = null;
            if (doc != null)
            {
                foreach (XmlNode n in doc.GetElementsByTagName("*"))
                {
                    if (n.LocalName == "errorCode")
                    {
                        code = n.InnerText.Trim();
                    }
                    else if (n.LocalName == "errorDescription")
                    {
                        desc = n.InnerText.Trim();
                    }
                }
            }
            error = code != null ? $"{code} {desc}" : $"HTTP {status}";
            return null;
        }

        private static string Http(string method, string url, string body, string soapAction, out int status)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method;
            req.Timeout = req.ReadWriteTimeout = 3000;
            req.KeepAlive = false;
            req.Proxy = null; // the router is on the LAN: never through a proxy
            if (body != null)
            {
                byte[] b = Encoding.UTF8.GetBytes(body);
                req.ContentType = "text/xml; charset=\"utf-8\"";
                req.Headers["SOAPAction"] = soapAction;
                req.ContentLength = b.Length;
                using (var s = req.GetRequestStream())
                {
                    s.Write(b, 0, b.Length);
                }
            }
            HttpWebResponse resp;
            try { resp = (HttpWebResponse)req.GetResponse(); }
            catch (WebException e) when (e.Response is HttpWebResponse r) { resp = r; } // a SOAP fault comes as HTTP 500
            using (resp)
            {
                using (var rd = new StreamReader(resp.GetResponseStream()))
                {
                    status = (int)resp.StatusCode;
                    return rd.ReadToEnd();
                }
            }
        }

        /// <summary>Our address on the network that leads to the router (what it should forward to).</summary>
        private static string LocalAddressToward(string host, int port)
        {
            try
            {
                using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    s.Connect(Dns.GetHostAddresses(host)[0], port); // UDP: nothing is sent, the route is picked
                    return ((IPEndPoint)s.LocalEndPoint).Address.ToString();
                }
            }
            catch (Exception e) { Plugin.Log.LogInfo($"[port mapping] no route to {host}: {e.Message}"); return null; }
        }

        // ---- NAT-PMP (RFC 6886) --------------------------------------------

        private Mapping TryNatPmp(int port)
        {
            foreach (var gw in Gateways())
            {
                // Opcode 0: the external address; it also tells whether NAT-PMP is there at all.
                byte[] r = PmpAsk(gw, new byte[] { 0, 0 }, 12, 4);
                if (r == null || r[1] != 128 || (r[2] << 8 | r[3]) != 0)
                {
                    continue;
                }
                ExternalIp = new IPAddress(new[] { r[8], r[9], r[10], r[11] }).ToString();
                var m = new Mapping { Gateway = gw, Port = port, InternalIp = LocalAddressToward(gw.Address.ToString(), gw.Port) };
                m.ExternalTcp = PmpMap(gw, 2, port, port, LeaseSeconds);
                if (m.ExternalTcp <= 0) { Plugin.Log.LogInfo($"[port mapping] NAT-PMP at {gw} refused TCP {port}"); continue; }
                m.ExternalUdp = Math.Max(0, PmpMap(gw, 1, port, port, LeaseSeconds));
                return m;
            }
            return null;
        }

        /// <summary>Maps (or with a lifetime of 0, removes) a port; the external port the router gave, or -1.</summary>
        private static int PmpMap(IPEndPoint gw, byte op, int internalPort, int externalPort, int lifetime, int tries = 4)
        {
            var q = new byte[12];
            q[1] = op;
            q[4] = (byte)(internalPort >> 8); q[5] = (byte)internalPort;
            q[6] = (byte)(externalPort >> 8); q[7] = (byte)externalPort;
            q[8] = (byte)(lifetime >> 24); q[9] = (byte)(lifetime >> 16); q[10] = (byte)(lifetime >> 8); q[11] = (byte)lifetime;
            byte[] r = PmpAsk(gw, q, 16, tries);
            if (r == null || r[1] != 128 + op || (r[2] << 8 | r[3]) != 0)
            {
                return -1;
            }
            return r[10] << 8 | r[11];
        }

        /// <summary>Sends a request and waits for an answer of at least <paramref name="min"/> bytes (250 ms, doubling, as RFC 6886 says).</summary>
        private static byte[] PmpAsk(IPEndPoint gw, byte[] q, int min, int tries)
        {
            using (var u = new UdpClient(gw.AddressFamily))
            {
                int wait = 250;
                for (int i = 0; i < tries; i++, wait *= 2)
                {
                    u.Send(q, q.Length, gw);
                    u.Client.ReceiveTimeout = wait;
                    try
                    {
                        var from = new IPEndPoint(IPAddress.Any, 0);
                        byte[] r = u.Receive(ref from);
                        if (from.Address.Equals(gw.Address) && r.Length >= min && r[1] == q[1] + 128)
                        {
                            return r;
                        }
                    }
                    catch (SocketException) { }
                }
            }
            return null;
        }

        /// <summary>The NAT-PMP endpoints: the default gateways on port 5351, or the test router.</summary>
        private static List<IPEndPoint> Gateways()
        {
            if (TestRouter != null && TcpBackend.TryParseAddress(TestRouter, out string th, out int tp))
            {
                return new List<IPEndPoint> { new IPEndPoint(IPAddress.Parse(th), tp) };
            }
            return GatewayAddresses().ConvertAll(a => new IPEndPoint(a, 5351));
        }

        /// <summary>The default gateways (IPv4): Mono's list, or Linux's route table if that's empty.</summary>
        private static List<IPAddress> GatewayAddresses()
        {
            var list = new List<IPAddress>();
            void Add(IPAddress a)
            {
                if (a != null && a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any) && !list.Contains(a))
                {
                    list.Add(a);
                }
            }
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up)
                    {
                        continue;
                    }
                    foreach (var g in nic.GetIPProperties().GatewayAddresses)
                    {
                        Add(g.Address);
                    }
                }
            }
            catch (Exception e) { Plugin.Log.LogInfo("[port mapping] couldn't list gateways: " + e.Message); }
            if (list.Count == 0 && File.Exists("/proc/net/route"))
            {
                try
                {
                    // "Iface Destination Gateway ...", hex in host (little-endian) order; the default route's destination is 0.
                    foreach (string line in File.ReadAllLines("/proc/net/route"))
                    {
                        var f = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (f.Length < 3 || f[1] != "00000000")
                        {
                            continue;
                        }
                        if (uint.TryParse(f[2], System.Globalization.NumberStyles.HexNumber, null, out uint g) && g != 0)
                        {
                            Add(new IPAddress(g));
                        }
                    }
                }
                catch { }
            }
            return list;
        }
    }
}
