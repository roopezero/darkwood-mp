using System.Collections.Generic;
using DarkwoodMP.Net;

namespace DarkwoodMP.World.Story
{
    /// The bytes of MsgType.FlagsDelta (either way): u16 n, then n x (str
    /// key, str value), the story entries that changed (StoryKeys says what
    /// they name).
    public static class FlagsDelta
    {
        /// The payload, after Begin(MsgType.FlagsDelta): every entry given.
        /// The shell gives at most StoryRules.PerMessage at a time.
        public static void Write(
            PacketWriter w,
            IReadOnlyList<KeyValuePair<string, string>> entries)
        {
            w.W.Write((ushort)entries.Count);
            foreach (var kv in entries)
            {
                w.WriteStr(kv.Key);
                w.WriteStr(kv.Value);
            }
        }

        /// The entries, in the order they were written.
        public static List<KeyValuePair<string, string>> Read(PacketReader r)
        {
            int n = r.R.ReadUInt16();
            var got = new List<KeyValuePair<string, string>>();
            for (int i = 0; i < n; i++)
            {
                string key = r.ReadStr();
                string value = r.ReadStr();
                got.Add(new KeyValuePair<string, string>(key, value));
            }

            return got;
        }
    }
}
