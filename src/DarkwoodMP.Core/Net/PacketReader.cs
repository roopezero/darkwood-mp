using System.IO;
using System.Text;

namespace DarkwoodMP.Net
{
    public sealed class PacketReader
    {
        private readonly BinaryReader _r;
        public MsgType Type { get; }

        public PacketReader(byte[] data, int length)
        {
            _r = new BinaryReader(
                new MemoryStream(data, 0, length, false), Encoding.UTF8);
            Type = (MsgType)_r.ReadByte();
        }

        public BinaryReader R => _r;
        public Vec3 ReadVec3() =>
            new Vec3(_r.ReadSingle(), _r.ReadSingle(), _r.ReadSingle());

        public string ReadStr()
        {
            int n = _r.ReadUInt16();
            return Encoding.UTF8.GetString(_r.ReadBytes(n));
        }

        public string ReadBigStr()
        {
            uint n = _r.ReadUInt32();
            if (n > Protocol.MaxMessage)
            {
                throw new InvalidDataException($"bigstr of {n} bytes");
            }

            var b = _r.ReadBytes((int)n);
            if (b.Length != n)
            {
                throw new EndOfStreamException();
            }

            return Encoding.UTF8.GetString(b);
        }
    }
}
