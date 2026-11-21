using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World;
using DarkwoodMP.World.Inventories;

namespace DarkwoodMP.Tests.World.Inventories
{
    public class ContainerSlotsAskMessageTests
    {
        static readonly ObjName Storage = new ObjName(ObjKind.Saved, 9);

        static PacketWriter Written()
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.ContainerSlotsAsk);
            ContainerSlotsAskMessage.Write(w, Storage);
            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_name_alone()
            {
                Bytes.Of(Written()).ShouldBe(new byte[]
                {
                    0xC8,
                    1,
                    9, 0, 0, 0, 0, 0, 0, 0,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_the_name()
            {
                ContainerSlotsAskMessage.Read(Bytes.Back(Written()))
                    .ShouldBe(Storage);
            }
        }
    }
}
