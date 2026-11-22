using System;
using System.Collections.Generic;
using DarkwoodMP.Net;

namespace DarkwoodMP.Tests.Net
{
    /// A host and its clients in one process, over a LoopbackHub, with one
    /// clock the test moves.
    public sealed class LoopbackGame
    {
        public const string ModVersion = "1.2.3";

        public readonly LoopbackHub Hub = new LoopbackHub();
        public readonly FakeClock Clock = new FakeClock();
        public readonly RecordingLog Log = new RecordingLog();

        private readonly List<Session> _sessions = new List<Session>();
        private readonly List<RawPeer> _raws = new List<RawPeer>();
        private readonly Dictionary<Session, LoopbackBackend> _backends =
            new Dictionary<Session, LoopbackBackend>();

        /// A player key Session accepts (32 characters), one per name.
        public static string KeyOf(string name) =>
            (name + new string('k', 32)).Substring(0, 32);

        /// A Session with its backend, not in a game yet.
        public Session Player(string name)
        {
            var session = new Session(Clock, Log, ModVersion);
            session.LocalName = name;
            session.LocalKey = KeyOf(name);
            var backend = Hub.NewBackend();
            session.Add(backend);
            _backends[session] = backend;
            _sessions.Add(session);
            return session;
        }

        /// A Session hosting with this password, at most this many players.
        public Session Host(string password, int maxPlayers)
        {
            var host = Player("Host");
            Backend(host).Host(password, maxPlayers);
            return host;
        }

        /// A Session hosting, with no password and room for five.
        public Session Host() => Host("", Protocol.MaxPlayers);

        /// A Session that joins (once the game runs) with this password.
        public Session Join(string name, string password)
        {
            var client = Player(name);
            Backend(client).Join(password);
            return client;
        }

        public Session Join(string name) => Join(name, "");

        /// A backend with no Session: the test sends its bytes and reads
        /// what comes back.
        public RawPeer Raw()
        {
            var raw = new RawPeer(Hub.NewBackend());
            _raws.Add(raw);
            return raw;
        }

        /// The backend this game gave a Session.
        public LoopbackBackend Backend(Session session) =>
            _backends[session];

        /// Update every Session and poll every raw peer until nothing is on
        /// its way.
        public void Run()
        {
            for (int round = 0; round < 100; round++)
            {
                foreach (var s in _sessions)
                {
                    s.Update();
                }

                foreach (var r in _raws)
                {
                    r.Backend.Poll();
                }

                if (Hub.Idle)
                {
                    return;
                }
            }

            throw new InvalidOperationException("the game never went quiet");
        }

        /// Move both clocks on, then run.
        public void Wait(float seconds)
        {
            Clock.RealTime += seconds;
            Clock.UnscaledTime += seconds;
            Run();
        }
    }

    /// A peer the test drives by hand: what it sends and what it got are
    /// raw messages.
    public sealed class RawPeer
    {
        public readonly LoopbackBackend Backend;
        public readonly List<byte[]> Got = new List<byte[]>();

        public RawPeer(LoopbackBackend backend)
        {
            Backend = backend;
            Backend.OnMessage += (from, data, length) =>
            {
                var copy = new byte[length];
                Array.Copy(data, copy, length);
                Got.Add(copy);
            };
        }

        public void Send(PacketWriter w) =>
            Send(Bytes(w));

        /// Client: send to the host.
        public void Send(byte[] bytes) => SendTo(Backend.HostId, bytes);

        public void SendTo(ulong to, byte[] bytes) =>
            Backend.Send(to, bytes, bytes.Length, Channel.Control, true);

        public static byte[] Bytes(PacketWriter w)
        {
            var bytes = new byte[w.Length];
            Array.Copy(w.Buffer, bytes, w.Length);
            return bytes;
        }
    }
}
