using DarkwoodMP.Net;

namespace DarkwoodMP.World.Traders
{
    /// The bytes of MsgType.TraderAsk (client to host): which trader the
    /// client wants the stock of (TraderWho).
    public static class TraderAsk
    {
        /// The payload, after Begin(MsgType.TraderAsk).
        public static void Write(PacketWriter w, in TraderWho trader)
        {
            trader.Write(w);
        }

        public static TraderWho Read(PacketReader r)
        {
            return TraderWho.Read(r);
        }
    }
}
