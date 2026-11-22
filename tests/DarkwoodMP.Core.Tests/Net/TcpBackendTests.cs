using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using DarkwoodMP.Net;

namespace DarkwoodMP.Tests.Net
{
    /// Direct connections over real TCP on localhost: the u32 length in
    /// front of every message, the limits, and the info query over UDP.
    public class TcpBackendTests
    {
        private static readonly byte[] PingFrame =
            { 1, 0, 0, 0, (byte)MsgType.Ping };

        /// Poll until done, or fail after five seconds.
        private static void Until(Func<bool> done, params TcpBackend[] polls)
        {
            var clock = Stopwatch.StartNew();
            while (!done())
            {
                if (clock.ElapsedMilliseconds > 5000)
                {
                    throw new TimeoutException("waited 5 s");
                }

                foreach (var b in polls)
                {
                    b.Poll();
                }

                Thread.Sleep(2);
            }
        }

        /// A port that was free for both TCP and UDP a moment ago.
        private static int FreePort()
        {
            for (int tries = 0; tries < 20; tries++)
            {
                var probe = new TcpListener(IPAddress.Any, 0);
                probe.Start();
                int port = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();
                try
                {
                    new UdpClient(new IPEndPoint(IPAddress.Any, port))
                        .Dispose();
                    return port;
                }
                catch (SocketException)
                {
                }
            }

            throw new InvalidOperationException("no free port");
        }

        /// A backend hosting on a free port; before runs first (to add it
        /// to a Session).
        private static TcpBackend Hosting(
            string name, int maxPlayers, Action<TcpBackend> before)
        {
            for (int tries = 0; tries < 20; tries++)
            {
                var host = new TcpBackend(new RecordingLog(), "1.2.3");
                before(host);
                if (host.Host(name, FreePort(), "", maxPlayers))
                {
                    return host;
                }

                host.Dispose();
            }

            throw new InvalidOperationException("couldn't host");
        }

        private static TcpBackend Hosting(string name, int maxPlayers) =>
            Hosting(name, maxPlayers, NoSetup);

        // Nothing to do before hosting.
        private static void NoSetup(TcpBackend backend)
        {
        }

        /// A TcpBackend client connected to a plain socket the test reads
        /// and writes.
        private sealed class Wire : IDisposable
        {
            public readonly TcpBackend Client =
                new TcpBackend(new RecordingLog(), "1.2.3");
            public readonly NetworkStream Stream;
            private readonly TcpListener _listener =
                new TcpListener(IPAddress.Loopback, 0);
            private readonly TcpClient _peer;

            public Wire()
            {
                _listener.Start();
                int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                bool entered = false;
                Client.OnEntered += () => entered = true;
                Client.Join($"127.0.0.1:{port}", "");
                var accept = _listener.AcceptTcpClientAsync();
                if (!accept.Wait(5000))
                {
                    throw new TimeoutException("no connection");
                }

                _peer = accept.Result;
                _peer.ReceiveTimeout = 5000;
                Stream = _peer.GetStream();
                Until(() => entered, Client);
            }

            public byte[] Read(int n)
            {
                var buf = new byte[n];
                int got = 0;
                while (got < n)
                {
                    int k = Stream.Read(buf, got, n - got);
                    if (k <= 0)
                    {
                        throw new EndOfStreamException();
                    }

                    got += k;
                }

                return buf;
            }

            /// The next frame that isn't a Ping, its length included.
            public byte[] ReadFrame()
            {
                while (true)
                {
                    var head = Read(4);
                    int len = (int)BitConverter.ToUInt32(head, 0);
                    var body = Read(len);
                    if (len == 1 && body[0] == (byte)MsgType.Ping)
                    {
                        continue;
                    }

                    var frame = new byte[4 + len];
                    head.CopyTo(frame, 0);
                    body.CopyTo(frame, 4);
                    return frame;
                }
            }

            /// Close the test's end of the connection.
            public void Hangup() => _peer.Close();

            public void Write(params byte[] bytes) =>
                Stream.Write(bytes, 0, bytes.Length);

            public void Dispose()
            {
                Client.Dispose();
                _peer.Dispose();
                _listener.Stop();
            }
        }

        public class Send
        {
            [Fact]
            public void Starts_the_connection_with_a_ping()
            {
                using var wire = new Wire();

                var first = wire.Read(5);

                first.ShouldBe(PingFrame);
            }

            [Fact]
            public void Puts_the_u32_length_before_the_message()
            {
                using var wire = new Wire();
                var data = new byte[] { (byte)MsgType.PlayerLeft, 9, 8, 7 };

                wire.Client.Send(TcpBackend.HostPeerId, data, 3, 0, true);

                wire.ReadFrame().ShouldBe(
                    new byte[] { 3, 0, 0, 0, (byte)MsgType.PlayerLeft, 9, 8 });
            }

            [Fact]
            public void Writes_a_length_over_255_little_endian()
            {
                using var wire = new Wire();
                var data = new byte[300];
                data[0] = (byte)MsgType.PlayerLeft;

                wire.Client.Send(TcpBackend.HostPeerId, data, 300, 0, true);

                wire.ReadFrame()[..4].ShouldBe(new byte[] { 44, 1, 0, 0 });
            }

            [Fact]
            public void Refuses_a_peer_it_has_no_connection_to()
            {
                using var wire = new Wire();
                var data = new byte[] { (byte)MsgType.PlayerLeft };

                bool sent = wire.Client.Send(77, data, 1, 0, true);

                sent.ShouldBeFalse();
            }
        }

        public class Poll
        {
            [Fact]
            public void Hands_on_a_message_without_its_length()
            {
                using var wire = new Wire();
                var got = new List<byte[]>();
                wire.Client.OnMessage += (from, data, n) => got.Add(data);

                wire.Write(3, 0, 0, 0, (byte)MsgType.PlayerLeft, 7, 8);
                Until(() => got.Count > 0, wire.Client);

                got[0].ShouldBe(new byte[] { (byte)MsgType.PlayerLeft, 7, 8 });
            }

            [Fact]
            public void Says_a_message_came_from_the_host()
            {
                using var wire = new Wire();
                var from = new List<ulong>();
                wire.Client.OnMessage += (id, data, n) => from.Add(id);

                wire.Write(1, 0, 0, 0, (byte)MsgType.PlayerLeft);
                Until(() => from.Count > 0, wire.Client);

                from.ShouldBe(new[] { TcpBackend.HostPeerId });
            }

            [Fact]
            public void Keeps_a_ping_to_itself()
            {
                using var wire = new Wire();
                var got = new List<byte[]>();
                wire.Client.OnMessage += (from, data, n) => got.Add(data);

                wire.Write(PingFrame);
                wire.Write(1, 0, 0, 0, (byte)MsgType.PlayerLeft);
                Until(() => got.Count > 0, wire.Client);

                got.ShouldHaveSingleItem()[0]
                    .ShouldBe((byte)MsgType.PlayerLeft);
            }

            [Fact]
            public void Leaves_on_a_message_of_length_zero()
            {
                using var wire = new Wire();

                wire.Write(0, 0, 0, 0);
                Until(() => !wire.Client.InSession, wire.Client);

                wire.Client.Status.ShouldBe(
                    "Disconnected from host: bad message length 0");
            }

            [Fact]
            public void Counts_a_closed_connection_as_the_host_lost()
            {
                using var wire = new Wire();

                wire.Hangup();
                Until(() => !wire.Client.InSession, wire.Client);

                wire.Client.HostLost.ShouldBeTrue();
            }
        }

        public class Host
        {
            [Fact]
            public void Answers_the_info_query()
            {
                using var host = Hosting("Night", 5);
                using var udp = new UdpClient(0);
                udp.Client.ReceiveTimeout = 5000;
                var to = new IPEndPoint(IPAddress.Loopback, host.Port);

                udp.Send(ServerInfo.Query, ServerInfo.Query.Length, to);
                var from = new IPEndPoint(IPAddress.Any, 0);
                var answer = udp.Receive(ref from);

                answer.ShouldBe(InfoDatagram(
                    "INFO|Night|1|5|0|0|1.2.3|game=darkwood|proto="
                        + Protocol.Version + "|"));
            }

            [Fact]
            public void Names_a_server_with_no_name_Darkwood()
            {
                using var host = Hosting("  ", 5);

                host.ServerName.ShouldBe("Darkwood");
            }

            [Fact]
            public void Holds_at_most_the_protocols_players()
            {
                using var host = Hosting("Night", 99);

                host.MaxPlayers.ShouldBe(Protocol.MaxPlayers);
            }

            [Fact]
            public void Holds_at_least_two_players()
            {
                using var host = Hosting("Night", 1);

                host.MaxPlayers.ShouldBe(2);
            }

            [Fact]
            public void Drops_a_first_message_over_64_KiB()
            {
                using var host = Hosting("Night", 5);
                var lost = new List<ulong>();
                host.OnPeerLost += lost.Add;
                using var peer = new TcpClient();
                peer.Connect(IPAddress.Loopback, host.Port);

                peer.GetStream().Write(new byte[] { 1, 0, 1, 0 }, 0, 4);
                Until(() => lost.Count > 0, host);

                lost.Count.ShouldBe(1);
            }

            private static byte[] InfoDatagram(string text)
            {
                var b = System.Text.Encoding.UTF8.GetBytes(text + "#");
                for (int i = 0; i < b.Length; i++)
                {
                    if (b[i] == (byte)'|')
                    {
                        b[i] = ServerInfo.Sep;
                    }
                    else if (b[i] == (byte)'#')
                    {
                        b[i] = ServerInfo.End;
                    }
                }

                return b;
            }
        }

        [Collection(SessionCollection.Name)]
        public class Join
        {
            [Fact]
            public void Gets_a_session_welcomed_over_tcp()
            {
                var clock = new FakeClock();
                using var host = new Session(clock, new RecordingLog(), "1");
                host.LocalKey = LoopbackGame.KeyOf("Host");
                var hosting = Hosting("Night", 5, host.Add);
                using var joiner = new Session(
                    clock, new RecordingLog(), "1");
                joiner.LocalKey = LoopbackGame.KeyOf("Ann");
                var client = new TcpBackend(new RecordingLog(), "1");
                joiner.Add(client);

                client.Join($"127.0.0.1:{hosting.Port}", "");
                var time = Stopwatch.StartNew();
                while (!joiner.Connected && time.ElapsedMilliseconds < 5000)
                {
                    host.Update();
                    joiner.Update();
                    Thread.Sleep(2);
                }

                joiner.Connected.ShouldBeTrue();
            }

            [Fact]
            public void Turns_down_port_zero()
            {
                using var client = new TcpBackend(new RecordingLog(), "1");

                client.Join("example.com:0", "");

                client.Status.ShouldBe(
                    "Enter an address like 192.168.1.20 or example.com:27720");
            }
        }

        public class TryParseAddress
        {
            [Fact]
            public void Fills_in_the_default_port()
            {
                TcpBackend.TryParseAddress("example.com", out _, out int port);

                port.ShouldBe(TcpBackend.DefaultPort);
            }

            [Fact]
            public void Reads_the_port_after_the_colon()
            {
                TcpBackend.TryParseAddress("10.0.0.2:4000", out _, out int p);

                p.ShouldBe(4000);
            }

            [Fact]
            public void Refuses_a_port_past_65535()
            {
                bool ok = TcpBackend.TryParseAddress("h:70000", out _, out _);

                ok.ShouldBeFalse();
            }
        }

        public class Normalize
        {
            [Fact]
            public void Writes_host_and_port()
            {
                TcpBackend.Normalize(" example.com ")
                    .ShouldBe("example.com:27720");
            }
        }
    }
}
