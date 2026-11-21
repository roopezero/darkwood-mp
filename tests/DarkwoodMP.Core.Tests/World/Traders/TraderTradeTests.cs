using System.Collections.Generic;
using DarkwoodMP.Net;
using DarkwoodMP.World.Traders;
using static DarkwoodMP.Tests.World.Traders.TraderBytes;

namespace DarkwoodMP.Tests.World.Traders
{
    public class TraderTradeTests
    {
        /// Took 2 "c", gave nothing, an empty stock.
        static Trade Traded(int change)
        {
            return new Trade
            {
                Trader = Wolfman,
                Took = new Dictionary<string, int> { ["c"] = 2 },
                Gave = new Dictionary<string, int>(),
                Stock = "",
                Change = change,
            };
        }

        public class Write
        {
            [Fact]
            public void Writes_took_gave_the_stock_and_the_change()
            {
                var w = Begun(MsgType.TraderTrade);

                TraderTrade.Write(w, Traded(-1));

                Bytes(w).ShouldBe(Joined(
                    new byte[] { 0xD8 },
                    WolfmanBytes,
                    new byte[] { 1, 0, 1, 0, 99, 2, 0, 0, 0 },
                    new byte[] { 0, 0 },
                    new byte[] { 0, 0, 0, 0 },
                    new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }));
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Begun(MsgType.TraderTrade);
                TraderTrade.Write(w, Traded(5));

                var trade = TraderTrade.Read(ReaderOf(w));

                trade.Trader.Name.ShouldBe("ab");
                trade.Took["c"].ShouldBe(2);
                trade.Gave.Count.ShouldBe(0);
                trade.Stock.ShouldBe("");
                trade.Change.ShouldBe(5);
            }

            [Fact]
            public void Adds_up_a_type_that_comes_twice()
            {
                var w = Begun(MsgType.TraderTrade);
                Wolfman.Write(w);
                w.W.Write((ushort)2);
                w.WriteStr("c");
                w.W.Write(2);
                w.WriteStr("c");
                w.W.Write(3);
                w.W.Write((ushort)0);
                w.WriteBigStr("");
                w.W.Write(0);

                var trade = TraderTrade.Read(ReaderOf(w));

                trade.Took["c"].ShouldBe(5);
            }
        }
    }
}
