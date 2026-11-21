using System;
using DarkwoodMP.Net;

namespace DarkwoodMP.Tests.World.Inventories
{
    /// The bytes a writer holds, for the message tests.
    static class Bytes
    {
        public static byte[] Of(PacketWriter w)
        {
            var bytes = new byte[w.Length];
            Array.Copy(w.Buffer, bytes, w.Length);
            return bytes;
        }

        public static PacketReader Back(PacketWriter w) =>
            new PacketReader(w.Buffer, w.Length);
    }
}
