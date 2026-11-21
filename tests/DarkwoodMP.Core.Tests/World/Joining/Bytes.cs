using System;
using DarkwoodMP.Net;

namespace DarkwoodMP.Tests.World.Joining
{
    static class Bytes
    {
        public static byte[] Of(PacketWriter w)
        {
            var bytes = new byte[w.Length];
            Array.Copy(w.Buffer, bytes, w.Length);
            return bytes;
        }

        /// 32 bytes, each its index plus first.
        public static byte[] Hash(byte first)
        {
            var hash = new byte[32];
            for (int i = 0; i < hash.Length; i++)
            {
                hash[i] = (byte)(first + i);
            }

            return hash;
        }
    }
}
