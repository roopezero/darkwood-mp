using DarkwoodMP.Net;

namespace DarkwoodMP.Combat.Joiners
{
    /// The bytes of MsgType.ViewRect (client to host): u16 half width, u16
    /// half height of the area the client's game keeps running.
    public static class ViewRectMessage
    {
        /// The payload, after Begin(MsgType.ViewRect). Each half is clamped
        /// to 0 to 65535 to fit its u16.
        public static void Write(PacketWriter w, ViewRect rect)
        {
            w.W.Write(Half(rect.Dx));
            w.W.Write(Half(rect.Dy));
        }

        /// The rect as the client reported it, not yet clamped
        /// (ViewRectRules.Accepted).
        public static ViewRect Read(PacketReader r)
        {
            int dx = r.R.ReadUInt16();
            int dy = r.R.ReadUInt16();
            return new ViewRect(dx, dy);
        }

        static ushort Half(int v)
        {
            if (v < 0)
            {
                return 0;
            }

            if (v > ushort.MaxValue)
            {
                return ushort.MaxValue;
            }

            return (ushort)v;
        }
    }
}
