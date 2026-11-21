using DarkwoodMP.Net;

namespace DarkwoodMP.Combat.Enemies
{
    /// The bytes of MsgType.EnemyBlood (host -> client): u32 id, f32
    /// rotation, u8 in water.
    public static class EnemyBloodMessage
    {
        /// The payload, after Begin(MsgType.EnemyBlood).
        public static void Write(PacketWriter w, in EnemyBlood b)
        {
            w.W.Write(b.Id);
            w.W.Write(b.RotY);
            w.W.Write(Flag.Of(b.Water));
        }

        public static EnemyBlood Read(PacketReader r)
        {
            uint id = r.R.ReadUInt32();
            float rotY = r.R.ReadSingle();
            bool water = r.R.ReadByte() != 0;
            return new EnemyBlood
            {
                Id = id,
                RotY = rotY,
                Water = water,
            };
        }
    }
}
