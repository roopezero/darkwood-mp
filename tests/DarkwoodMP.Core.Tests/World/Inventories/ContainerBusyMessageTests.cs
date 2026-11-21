using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World;
using DarkwoodMP.World.Inventories;

namespace DarkwoodMP.Tests.World.Inventories
{
    public class ContainerBusyMessageTests
    {
        static readonly ObjName Chest = new ObjName(ObjKind.Saved, 9);

        static PacketWriter Written(string who)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.ContainerBusy);
            ContainerBusyMessage.Write(w, Chest, who);
            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_name_then_who_has_it_open()
            {
                Bytes.Of(Written("Al")).ShouldBe(new byte[]
                {
                    29,
                    1,
                    9, 0, 0, 0, 0, 0, 0, 0,
                    2, 0, (byte)'A', (byte)'l',
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_the_name_and_who()
            {
                var r = Bytes.Back(Written("Al"));

                var box = ContainerBusyMessage.Read(r, out string who);

                box.ShouldBe(Chest);
                who.ShouldBe("Al");
            }
        }
    }
}
