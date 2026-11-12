using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DarkwoodMP.Net
{
    /// <summary>
    /// The server browser's info query, the same as goasis's (package protocol, after Half-Life's A2S_INFO):
    /// one UDP datagram "info" SEP END to the game's port number, answered with one datagram
    ///   INFO SEP name SEP players SEP maxplayers SEP password SEP localchars SEP version SEP [key=value SEP]* END
    /// where SEP is byte 0 and END is byte 237. DarkwoodMP adds the fields game=darkwood and proto=&lt;version&gt;.
    /// </summary>
    public sealed class ServerInfo
    {
        public const byte Sep = 0;
        public const byte End = 237;

        public string Name = "";
        public int Players;
        public int MaxPlayers;
        public bool Password;
        public bool LocalChars;
        public string Version = "";
        public readonly SortedDictionary<string, string> Fields = new SortedDictionary<string, string>(System.StringComparer.Ordinal);

        public string Game => Fields.TryGetValue("game", out var g) ? g : "";
        public int Proto => Fields.TryGetValue("proto", out var p) && int.TryParse(p, out int v) ? v : 0;
        /// <summary>Whether this build can join it, and why not.</summary>
        public string Incompatibility =>
            Game != Protocol.Game ? "not a Darkwood server"
            : Proto != Protocol.Version ? $"different DarkwoodMP version ({Version})"
            : null;

        public static byte[] Encode(string name, params string[] fields)
        {
            var b = new List<byte>(64);
            b.AddRange(Encoding.UTF8.GetBytes(name));
            b.Add(Sep);
            foreach (var f in fields) { b.AddRange(Encoding.UTF8.GetBytes(f)); b.Add(Sep); }
            b.Add(End);
            return b.ToArray();
        }

        public static readonly byte[] Query = Encode("info");

        public static bool IsQuery(byte[] b, int n) => n == Query.Length && b.Take(n).SequenceEqual(Query);

        private static string Clean(string s, string drop) =>
            new string((s ?? "").Where(c => c != (char)Sep && drop.IndexOf(c) < 0).ToArray());

        public byte[] EncodeAnswer()
        {
            var f = new List<string> { Clean(Name, ""), Players.ToString(), MaxPlayers.ToString(), Password ? "1" : "0", LocalChars ? "1" : "0", Clean(Version, "") };
            foreach (var kv in Fields)
            {
                string k = Clean(kv.Key, "=");
                if (k.Length > 0) f.Add(k + "=" + Clean(kv.Value, ""));
            }
            return Encode("INFO", f.ToArray());
        }

        public static ServerInfo Parse(byte[] b, int n)
        {
            if (n < 1 || b[n - 1] != End) return null;
            var parts = Encoding.UTF8.GetString(b, 0, n - 1).Split((char)Sep);
            if (parts.Length < 7 || parts[0] != "INFO") return null;
            if (!int.TryParse(parts[2], out int players) || !int.TryParse(parts[3], out int max)) return null;
            var info = new ServerInfo
            {
                Name = parts[1], Players = players, MaxPlayers = max,
                Password = parts[4] == "1", LocalChars = parts[5] == "1", Version = parts[6],
            };
            for (int i = 7; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf('=');
                if (eq > 0) info.Fields[parts[i].Substring(0, eq)] = parts[i].Substring(eq + 1);
            }
            return info;
        }
    }
}
