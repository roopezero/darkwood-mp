using DarkwoodMP.Net;

namespace DarkwoodMP.World.Windows
{
    /// The window part of MsgType.ObjState (host to a client), after the
    /// ObjRef and the kind byte (2, a window): u8 flags (1 barricaded),
    /// i32 barricade health.
    public static class WindowObjState
    {
        public const byte Barricaded = 1;

        public static void Write(PacketWriter w, in HostWindow window)
        {
            int flags = 0;
            if (window.Barricaded)
            {
                flags = Barricaded;
            }

            w.W.Write((byte)flags);
            w.W.Write(window.BarricadeHealth);
        }

        public static HostWindow Read(PacketReader r)
        {
            bool barricaded = (r.R.ReadByte() & Barricaded) != 0;
            return new HostWindow
            {
                Barricaded = barricaded,
                BarricadeHealth = r.R.ReadInt32(),
            };
        }
    }
}
