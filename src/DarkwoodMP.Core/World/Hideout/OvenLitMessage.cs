using DarkwoodMP.Net;

namespace DarkwoodMP.World.Hideout
{
    /// The bytes of MsgType.OvenLit (either way): ref the oven lit, ref the
    /// oven that goes out (kind None for none).
    public static class OvenLitMessage
    {
        /// The payload, after Begin(MsgType.OvenLit).
        public static void Write(PacketWriter w, in LitOven oven)
        {
            oven.Lit.Write(w);
            oven.Out.Write(w);
        }

        public static LitOven Read(PacketReader r)
        {
            ObjName lit = ObjName.Read(r);
            ObjName off = ObjName.Read(r);
            return new LitOven { Lit = lit, Out = off };
        }
    }
}
