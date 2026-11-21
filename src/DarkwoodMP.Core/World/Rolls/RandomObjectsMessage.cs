using System.Collections.Generic;
using DarkwoodMP.Net;

namespace DarkwoodMP.World.Rolls
{
    /// The bytes of MsgType.RandomObjects (host to client): u8 kind, then 1
    /// (the dawn's random objects, all of them): u16 n, n x (i64 id, u16
    /// chunk, u16 object, vec3 position in the chunk, str contents); or 2
    /// (a night mushroom): i64 id, str prefab, vec3 pos.
    public static class RandomObjectsMessage
    {
        public const byte Dawn = 1;
        public const byte Night = 2;

        /// The payload of the dawn's objects, after
        /// Begin(MsgType.RandomObjects).
        public static void WriteDawn(
            PacketWriter w, IReadOnlyList<DawnObject> all)
        {
            w.W.Write(Dawn);
            w.W.Write((ushort)all.Count);
            foreach (var o in all)
            {
                w.W.Write(o.Id);
                w.W.Write((ushort)o.Chunk);
                w.W.Write((ushort)o.Object);
                w.Write(o.Local);
                w.WriteStr(o.Contents);
            }
        }

        /// The payload of a night mushroom, after
        /// Begin(MsgType.RandomObjects).
        public static void WriteNight(PacketWriter w, in NightMushroom m)
        {
            w.W.Write(Night);
            w.W.Write(m.Id);
            w.WriteStr(m.Prefab);
            w.Write(m.Pos);
        }

        public static RandomObjectsBody Read(PacketReader r)
        {
            byte kind = r.R.ReadByte();
            if (kind == Dawn)
            {
                return new RandomObjectsBody
                {
                    Kind = kind,
                    Dawn = ReadDawn(r),
                };
            }

            if (kind == Night)
            {
                long id = r.R.ReadInt64();
                string prefab = r.ReadStr();
                Vec3 pos = r.ReadVec3();
                var m = new NightMushroom
                {
                    Id = id,
                    Prefab = prefab,
                    Pos = pos,
                };

                return new RandomObjectsBody { Kind = kind, Night = m };
            }

            return new RandomObjectsBody { Kind = kind };
        }

        static List<DawnObject> ReadDawn(PacketReader r)
        {
            int n = r.R.ReadUInt16();
            var all = new List<DawnObject>(n);
            for (int i = 0; i < n; i++)
            {
                long id = r.R.ReadInt64();
                int chunk = r.R.ReadUInt16();
                int obj = r.R.ReadUInt16();
                Vec3 local = r.ReadVec3();
                string contents = r.ReadStr();
                all.Add(new DawnObject
                {
                    Id = id,
                    Chunk = chunk,
                    Object = obj,
                    Local = local,
                    Contents = contents,
                });
            }

            return all;
        }
    }
}
