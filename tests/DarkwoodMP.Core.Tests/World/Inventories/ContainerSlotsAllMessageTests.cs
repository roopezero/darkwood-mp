using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World;
using DarkwoodMP.World.Inventories;

namespace DarkwoodMP.Tests.World.Inventories
{
    public class ContainerSlotsAllMessageTests
    {
        static readonly ObjName Chest = new ObjName(ObjKind.Saved, 5);

        static PacketWriter Written(string contents)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.ContainerSlotsAll);
            ContainerSlotsAllMessage.Write(w, Chest, contents);
            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_name_then_the_contents_as_a_bigstr()
            {
                Bytes.Of(Written("{}")).ShouldBe(new byte[]
                {
                    0xC9,
                    1,
                    5, 0, 0, 0, 0, 0, 0, 0,
                    2, 0, 0, 0, (byte)'{', (byte)'}',
                });
            }

            [Fact]
            public void Writes_an_empty_bigstr_when_the_host_has_none()
            {
                Bytes.Of(Written("")).Length.ShouldBe(14);
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_the_name_and_the_contents()
            {
                var r = Bytes.Back(Written("{\"a\":1}"));

                var box = ContainerSlotsAllMessage.Read(
                    r, out string contents);

                box.ShouldBe(Chest);
                contents.ShouldBe("{\"a\":1}");
            }
        }
    }
}
