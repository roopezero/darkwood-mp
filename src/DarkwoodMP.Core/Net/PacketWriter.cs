using System;
using System.IO;
using System.Text;

namespace DarkwoodMP.Net
{
    /// <summary>
    /// Builds one message: type byte, then the payload. BinaryWriter is
    /// little-endian on every platform.
    /// </summary>
    public sealed class PacketWriter
    {
        private readonly MemoryStream _ms = new MemoryStream(1024);
        private readonly BinaryWriter _w;
        private readonly ILog _log;

        public PacketWriter(ILog log)
        {
            _log = log;
            _w = new BinaryWriter(_ms, Encoding.UTF8);
        }

        public PacketWriter Begin(MsgType type)
        {
            _ms.SetLength(0);
            _w.Write((byte)type);
            return this;
        }

        public BinaryWriter W => _w;
        public byte[] Buffer => _ms.GetBuffer();
        public int Length => (int)_ms.Length;

        public void Write(Vec3 v)
        {
            _w.Write(v.X);
            _w.Write(v.Y);
            _w.Write(v.Z);
        }

        /// <summary>
        /// str: u16 length, then that many UTF-8 bytes. Longer text is cut (and
        /// logged): use WriteBigStr for JSON.
        /// </summary>
        public void WriteStr(string s)
        {
            string written = s;
            if (written == null)
            {
                written = "";
            }

            var b = Encoding.UTF8.GetBytes(written);
            int n = Math.Min(b.Length, ushort.MaxValue);
            if (n < b.Length)
            {
                _log.LogError(
                    $"A {b.Length}-byte string was cut to {n} bytes in a "
                        + "message: use WriteBigStr for "
                        + $"it\n{Environment.StackTrace}");
            }

            _w.Write((ushort)n);
            _w.Write(b, 0, n);
        }

        /// <summary>
        /// bigstr: u32 length, then that many UTF-8 bytes. For JSON
        /// (inventories, profiles), which can pass 64 KiB.
        /// </summary>
        public void WriteBigStr(string s)
        {
            string written = s;
            if (written == null)
            {
                written = "";
            }

            var b = Encoding.UTF8.GetBytes(written);
            _w.Write((uint)b.Length);
            _w.Write(b);
        }

        public void WriteBytes(byte[] b) => _w.Write(b);
    }
}
