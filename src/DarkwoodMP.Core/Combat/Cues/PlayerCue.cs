using DarkwoodMP.Net;

namespace DarkwoodMP.Combat.Cues
{
    /// The bytes of MsgType.PlayerCue (client to host): u8 kind
    /// (CueRules.Dodge or CueRules.Scare).
    public static class PlayerCue
    {
        /// The payload, after Begin(MsgType.PlayerCue).
        public static void Write(PacketWriter w, byte kind)
        {
            w.W.Write(kind);
        }

        /// The kind as the client sent it; CueRules.Reaction ignores an
        /// unknown one.
        public static byte Read(PacketReader r)
        {
            return r.R.ReadByte();
        }
    }
}
