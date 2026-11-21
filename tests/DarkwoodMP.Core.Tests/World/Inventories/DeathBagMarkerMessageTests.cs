using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World.Inventories;

namespace DarkwoodMP.Tests.World.Inventories
{
    public class DeathBagMarkerMessageTests
    {
        static PacketWriter Written()
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.DeathBagMarker);
            DeathBagMarkerMessage.Write(w, 3, new Vec3(1f, -2f, 0.5f));
            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_bag_id_then_where_the_marker_stands()
            {
                Bytes.Of(Written()).ShouldBe(new byte[]
                {
                    130,
                    3, 0, 0, 0, 0, 0, 0, 0,
                    0x00, 0x00, 0x80, 0x3F,
                    0x00, 0x00, 0x00, 0xC0,
                    0x00, 0x00, 0x00, 0x3F,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_the_bag_and_the_spot()
            {
                long bag = DeathBagMarkerMessage.Read(
                    Bytes.Back(Written()),
                    out Vec3 marker);

                bag.ShouldBe(3L);
                marker.ShouldBe(new Vec3(1f, -2f, 0.5f));
            }
        }
    }
}
