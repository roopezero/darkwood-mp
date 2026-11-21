using DarkwoodMP.Net;

namespace DarkwoodMP.World.Hideout
{
    /// The bytes of MsgType.PorterDelivered (host to everyone): u8 source
    /// hideout id, u8 target hideout id, i64 package id, i64 spawner id,
    /// bigstr contents.
    public static class PorterDeliveredMessage
    {
        /// The payload, after Begin(MsgType.PorterDelivered).
        public static void Write(PacketWriter w, in PorterDelivery move)
        {
            w.W.Write((byte)move.Source);
            w.W.Write((byte)move.Target);
            w.W.Write(move.Id);
            w.W.Write(move.SpawnerId);
            w.WriteBigStr(move.Contents);
        }

        public static PorterDelivery Read(PacketReader r)
        {
            int source = r.R.ReadByte();
            int target = r.R.ReadByte();
            long id = r.R.ReadInt64();
            long spawner = r.R.ReadInt64();
            string contents = r.ReadBigStr();
            return new PorterDelivery
            {
                Source = source,
                Target = target,
                Id = id,
                SpawnerId = spawner,
                Contents = contents,
            };
        }
    }
}
