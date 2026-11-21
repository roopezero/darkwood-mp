using DarkwoodMP.Net;

namespace DarkwoodMP.World.Joining
{
    /// The bytes of MsgType.NeedSave (client to host): 32 bytes hash,
    /// u32 offset, u32 length.
    public static class NeedSave
    {
        /// The payload, after Begin(MsgType.NeedSave).
        public static void Write(PacketWriter w, in WorldPiece piece)
        {
            w.WriteBytes(piece.Hash);
            w.W.Write(piece.Offset);
            w.W.Write(piece.Length);
        }

        public static WorldPiece Read(PacketReader r)
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
    }
}
