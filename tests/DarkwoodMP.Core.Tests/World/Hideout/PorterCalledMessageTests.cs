using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.Tests.World.Inventories;
using DarkwoodMP.World.Hideout;

namespace DarkwoodMP.Tests.World.Hideout
{
    public class PorterCalledMessageTests
    {
        static readonly Vec3 Place = new Vec3(1f, -2f, 0.5f);

        static PacketWriter Written(Vec3 place)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.PorterCalled);
            PorterCalledMessage.Write(w, place);
            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_place_as_three_floats()
            {
                Bytes.Of(Written(Place)).ShouldBe(new byte[]
                {
                    220,
                    0x00, 0x00, 0x80, 0x3F,
                    0x00, 0x00, 0x00, 0xC0,
                    0x00, 0x00, 0x00, 0x3F,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_the_place()
            {
                PorterCalledMessage.Read(Bytes.Back(Written(Place)))
                    .ShouldBe(Place);
            }
        }
    }
}
