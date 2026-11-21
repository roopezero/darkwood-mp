using System.Collections.Generic;
using System.Linq;
using System.Text;
using DarkwoodMP.Net;

namespace DarkwoodMP.Tests.Net
{
    /// The session over a LoopbackHub: the handshake, relaying, and what it
    /// does when players come and go. Split over SessionTests.*.cs.
    public partial class SessionTests
    {
        /// What a hand-written Hello says; by default one the host lets in.
        public sealed record HelloFacts
        {
            public ushort Version { get; init; } = Protocol.Version;
            public string Game { get; init; } = Protocol.Game;
            public string Key { get; init; } = LoopbackGame.KeyOf("Raw");
            public string Password { get; init; } = "";
            public string ModVersion { get; init; } = "0.1.0";
            public string Name { get; init; } = "Raw";

            public byte[] Bytes()
            {
                var w = new PacketWriter(new RecordingLog());
                w.Begin(MsgType.Hello);
                w.W.Write(Version);
                w.WriteStr(Game);
                w.WriteStr(Key);
                w.WriteStr(Password);
                w.WriteStr(ModVersion);
                w.WriteStr(Name);
                w.WriteBytes(new byte[32]);
                return RawPeer.Bytes(w);
            }
        }

        private static readonly HelloFacts Valid = new HelloFacts();

        /// A raw client connected to the game's host, not yet in.
        private static RawPeer Connected(LoopbackGame game)
        {
            var raw = game.Raw();
            raw.Backend.Join("");
            game.Run();
            return raw;
        }

        /// A raw client that the game's host let in.
        private static RawPeer Admitted(LoopbackGame game, string name)
        {
            var raw = Connected(game);
            raw.Send((Valid with
            {
                Name = name,
                Key = LoopbackGame.KeyOf(name),
            }).Bytes());

            game.Run();
            raw.Got.Clear();
            return raw;
        }

        /// The reason in the first Refused this peer got (null: none).
        private static string RefusalOf(RawPeer raw)
        {
            foreach (var m in raw.Got)
            {
                if (m[0] == (byte)MsgType.Refused)
                {
                    var r = new PacketReader(m, m.Length);
                    return r.ReadStr();
                }
            }

            return null;
        }

        /// The types of the messages this peer got, in order.
        private static List<MsgType> TypesOf(RawPeer raw) =>
            raw.Got.Select(m => (MsgType)m[0]).ToList();

        // ---- expected bytes ------------------------------------------------

        private static IEnumerable<byte> U16(int v) =>
            new[] { (byte)v, (byte)(v >> 8) };

        private static IEnumerable<byte> U64(ulong v) =>
            Enumerable.Range(0, 8).Select(i => (byte)(v >> (8 * i)));

        private static IEnumerable<byte> Str(string s)
        {
            var b = Encoding.UTF8.GetBytes(s);
            return U16(b.Length).Concat(b);
        }

        private static byte[] Message(
            MsgType type, params IEnumerable<byte>[] parts)
        {
            var all = new List<byte> { (byte)type };
            foreach (var p in parts)
            {
                all.AddRange(p);
            }

            return all.ToArray();
        }
    }
}
