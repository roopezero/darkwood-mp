using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.Tests.World.Inventories;
using DarkwoodMP.World.Hideout;

namespace DarkwoodMP.Tests.World.Hideout
{
    public class PorterTransportMessageTests
    {
        static PacketWriter Written(int hideout)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.PorterTransport);
            PorterTransportMessage.Write(w, hideout);
            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_hideout_id_as_a_byte()
            {
                Bytes.Of(Written(3)).ShouldBe(new byte[] { 120, 3 });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_the_hideout_id()
            {
                PorterTransportMessage.Read(Bytes.Back(Written(200)))
                    .ShouldBe(200);
            }
        }
    }
}
