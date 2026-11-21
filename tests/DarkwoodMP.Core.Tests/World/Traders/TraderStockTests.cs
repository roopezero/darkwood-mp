using DarkwoodMP.Net;
using DarkwoodMP.World.Traders;
using static DarkwoodMP.Tests.World.Traders.TraderBytes;

namespace DarkwoodMP.Tests.World.Traders
{
    public class TraderStockTests
    {
        public class Write
        {
            [Fact]
            public void Writes_the_trader_then_the_stock_as_a_bigstr()
            {
                var w = Begun(MsgType.TraderStock);

                TraderStock.Write(w, Wolfman, "x");

                Bytes(w).ShouldBe(Joined(
                    new byte[] { 46 },
                    WolfmanBytes,
                    new byte[] { 1, 0, 0, 0, 120 }));
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Begun(MsgType.TraderStock);
                TraderStock.Write(w, Wolfman, "{}");

                var trader = TraderStock.Read(ReaderOf(w), out string stock);

                trader.Name.ShouldBe("ab");
                stock.ShouldBe("{}");
            }
        }
    }
}
