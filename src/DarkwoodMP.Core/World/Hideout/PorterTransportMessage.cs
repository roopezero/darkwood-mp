using DarkwoodMP.Net;

namespace DarkwoodMP.World.Hideout
{
    /// The bytes of MsgType.PorterTransport (joiner to host): u8 the
    /// hideout id the porter takes things from.
    public static class PorterTransportMessage
    {
        /// The payload, after Begin(MsgType.PorterTransport).
        public static void Write(PacketWriter w, int hideout)
        {
            w.W.Write((byte)hideout);
        }

        public static int Read(PacketReader r)
        {
            return r.R.ReadByte();
        }
    }
}
