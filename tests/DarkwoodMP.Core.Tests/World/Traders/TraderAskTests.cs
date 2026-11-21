using DarkwoodMP.Net;
using DarkwoodMP.World;
using DarkwoodMP.World.Traders;
using static DarkwoodMP.Tests.World.Traders.TraderBytes;

namespace DarkwoodMP.Tests.World.Traders
{
    public class TraderAskTests
    {
        public class Write
        {
            [Fact]
            public void Writes_the_name_the_ref_and_x_and_z()
            {
                var w = Begun(MsgType.TraderAsk);

                TraderAsk.Write(w, Wolfman);

                Bytes(w).ShouldBe(Joined(new byte[] { 45 }, WolfmanBytes));
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Begun(MsgType.TraderAsk);
                TraderAsk.Write(w, Wolfman);

                var trader = TraderAsk.Read(ReaderOf(w));

                trader.Name.ShouldBe("ab");
                trader.Who.ShouldBe(new ObjName(ObjKind.Saved, 3));
                trader.At.ShouldBe(new Vec2(1f, 0.5f));
            }
        }
    }
}
