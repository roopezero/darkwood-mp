using DarkwoodMP.Net;

namespace DarkwoodMP.World.Traders
{
    /// The bytes of MsgType.TraderStock (host to clients): which trader
    /// (TraderWho), then bigstr his stock (an Inventory.SaveState's JSON).
    public static class TraderStock
    {
        /// The payload, after Begin(MsgType.TraderStock).
        public static void Write(
            PacketWriter w,
            in TraderWho trader,
            string stock)
        {
            trader.Write(w);
            w.WriteBigStr(stock);
        }

        public static TraderWho Read(PacketReader r, out string stock)
        {
            var trader = TraderWho.Read(r);
            stock = r.ReadBigStr();
            return trader;
        }
    }
}
