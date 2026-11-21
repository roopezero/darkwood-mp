using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.Tests.World.Inventories;
using DarkwoodMP.World.Hideout;

namespace DarkwoodMP.Tests.World.Hideout
{
    public class PorterDeliveredMessageTests
    {
        static readonly PorterDelivery Move = new PorterDelivery
        {
            Source = 2,
            Target = 5,
            Id = 4,
            SpawnerId = 0x0102030405060708L,
            Contents = "{}",
        };

        static PacketWriter Written(in PorterDelivery move)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.PorterDelivered);
            PorterDeliveredMessage.Write(w, move);
            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_each_field_in_order()
            {
                Bytes.Of(Written(Move)).ShouldBe(new byte[]
                {
                    121,
                    2,
                    5,
                    4, 0, 0, 0, 0, 0, 0, 0,
                    8, 7, 6, 5, 4, 3, 2, 1,
                    2, 0, 0, 0, (byte)'{', (byte)'}',
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_every_field()
            {
                var read =
                    PorterDeliveredMessage.Read(Bytes.Back(Written(Move)));

                read.ShouldBe(Move);
            }
        }
    }
}
