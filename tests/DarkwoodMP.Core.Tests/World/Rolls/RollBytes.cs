using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;

namespace DarkwoodMP.Tests.World.Rolls
{
    /// A writer for one of the rolls' messages, and its bytes.
    static class RollBytes
    {
        public static PacketWriter Begin(MsgType type)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(type);
            return w;
        }

        public static byte[] Of(PacketWriter w)
        {
            var bytes = new byte[w.Length];
            Array.Copy(w.Buffer, bytes, w.Length);
            return bytes;
        }

        /// A reader of what was written; it reads the type byte itself.
        public static PacketReader Reader(PacketWriter w)
        {
            return new PacketReader(w.Buffer, w.Length);
        }
    }
}
