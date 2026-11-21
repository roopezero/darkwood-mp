using DarkwoodMP.Net;

namespace DarkwoodMP.Combat.Enemies
{
    /// The bytes of MsgType.EnemyArmor and MsgType.EnemyDespawn (host ->
    /// client): u32 id, u8 on (armour put on, or the corpse kept: 1).
    public static class EnemyMarkMessage
    {
        /// The payload, after Begin(MsgType.EnemyArmor or EnemyDespawn).
        public static void Write(PacketWriter w, in EnemyMark m)
        {
            w.W.Write(m.Id);
            w.W.Write(Flag.Of(m.On));
        }

        public static EnemyMark Read(PacketReader r)
        {
            uint id = r.R.ReadUInt32();
            bool on = r.R.ReadByte() != 0;
            return new EnemyMark
            {
                Id = id,
                On = on,
            };
        }
    }
}
