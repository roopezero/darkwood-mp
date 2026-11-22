using DarkwoodMP.Net;

namespace DarkwoodMP.Combat.Effects
{
    /// The bytes of MsgType.CombatMusic (host -> client): u8 fight (1: one
    /// of the host's enemies is after the client's player).
    public static class CombatMusicMessage
    {
        /// The payload, after Begin(MsgType.CombatMusic).
        public static void Write(PacketWriter w, bool fight)
        {
            byte b = 0;
            if (fight)
            {
                b = 1;
            }

            w.W.Write(b);
        }

        public static bool Read(PacketReader r) => r.R.ReadByte() != 0;
    }
}
