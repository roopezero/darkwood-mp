using DarkwoodMP.Net;

namespace DarkwoodMP.World.Joining
{
    /// The bytes of MsgType.SaveData (host to client): 32 bytes hash,
    /// u32 offset, big data (u32 length + bytes).
    public static class SaveData
    {
        /// The payload, after Begin(MsgType.SaveData): the piece's head,
        /// then its bytes, taken from the world at the piece's offset.
        public static void Write(
            PacketWriter w,
            in WorldPiece piece,
            byte[] world)
        {
            w.WriteBytes(piece.Hash);
            w.W.Write(piece.Offset);
            w.W.Write(piece.Length);
            w.W.Write(world, (int)piece.Offset, (int)piece.Length);
        }

        /// The head: the hash, offset and length; the bytes follow it.
        public static WorldPiece ReadHead(PacketReader r)
        {
            var hash = r.R.ReadBytes(32);
            var offset = r.R.ReadUInt32();
            return new WorldPiece
            {
                Hash = hash,
                Offset = offset,
                Length = r.R.ReadUInt32(),
            };
        }

        /// The bytes after the head, into the world at the offset: false
        /// when fewer than length came.
        public static bool ReadInto(
            PacketReader r,
            byte[] world,
            in WorldPiece piece)
        {
            var n = (int)piece.Length;
            return r.R.Read(world, (int)piece.Offset, n) == n;
        }
    }
}
