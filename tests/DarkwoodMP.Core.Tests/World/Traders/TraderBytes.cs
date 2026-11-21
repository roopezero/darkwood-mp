using System;
using System.Collections.Generic;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World;
using DarkwoodMP.World.Traders;

namespace DarkwoodMP.Tests.World.Traders
{
    /// What the trader messages' tests share: a trader and his bytes.
    static class TraderBytes
    {
        public static readonly TraderWho Wolfman = new TraderWho
        {
            Name = "ab",
            Who = new ObjName(ObjKind.Saved, 3),
            At = new Vec2(1f, 0.5f),
        };

        /// Wolfman's bytes: "ab", Saved 3, x 1, z 0.5.
        public static readonly byte[] WolfmanBytes =
        {
            2, 0, 97, 98,
            1, 3, 0, 0, 0, 0, 0, 0, 0,
            0x00, 0x00, 0x80, 0x3F,
            0x00, 0x00, 0x00, 0x3F,
        };

        public static PacketWriter Begun(MsgType type)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(type);
            return w;
        }

        public static byte[] Bytes(PacketWriter w)
        {
            var bytes = new byte[w.Length];
            Array.Copy(w.Buffer, bytes, w.Length);
            return bytes;
        }

        public static byte[] Joined(params byte[][] parts)
        {
            var all = new List<byte>();
            foreach (var part in parts)
            {
                all.AddRange(part);
            }

            return all.ToArray();
        }

        public static PacketReader ReaderOf(PacketWriter w)
        {
            return new PacketReader(w.Buffer, w.Length);
        }
    }
}
