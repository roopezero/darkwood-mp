using System.Collections.Generic;
using DarkwoodMP.Net;

namespace DarkwoodMP.World
{
    /// The bytes of MsgType.HostIds (host to clients): u8 flags, u16
    /// count, then count times: i64 id, str name, vec3 where, i32 place.
    /// Named apart from the plugin's HostIds, which sends it.
    public static class HostIdsMessage
    {
        /// The most ids in one message; more go in several.
        public const int MaxPerMessage = 1000;

        /// The payload, after Begin(MsgType.HostIds): count ids of the
        /// list from start.
        public static void Write(PacketWriter w, byte flags, HostIdSlice ids)
        {
            w.W.Write(flags);
            w.W.Write((ushort)ids.Count);
            for (int i = ids.Start; i < ids.Start + ids.Count; i++)
            {
                HostIdEntry e = ids.List[i];
                w.W.Write(e.Id);
                w.WriteStr(e.Name);
                w.Write(e.Pos);
                w.W.Write(e.Place);
            }
        }

        /// The flags, and the ids in the order they came.
        public static List<HostIdEntry> Read(PacketReader r, out byte flags)
        {
            flags = r.R.ReadByte();
            int n = r.R.ReadUInt16();
            var ids = new List<HostIdEntry>(n);
            for (int i = 0; i < n; i++)
            {
                long id = r.R.ReadInt64();
                string name = r.ReadStr();
                Vec3 pos = r.ReadVec3();
                int place = r.R.ReadInt32();
                ids.Add(
                    new HostIdEntry
                    {
                        Id = id,
                        Name = name,
                        Pos = pos,
                        Place = place,
                    });
            }

            return ids;
        }
    }
}
