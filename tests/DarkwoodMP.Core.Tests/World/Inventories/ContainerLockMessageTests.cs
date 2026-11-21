using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World;
using DarkwoodMP.World.Inventories;

namespace DarkwoodMP.Tests.World.Inventories
{
    public class ContainerLockMessageTests
    {
        static readonly ObjName Bag = new ObjName(ObjKind.Spawned, 0x0102);

        static PacketWriter Written(bool open)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.ContainerLock);
            ContainerLockMessage.Write(w, Bag, open);
            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_name_then_1_when_opened()
            {
                Bytes.Of(Written(true)).ShouldBe(new byte[]
                {
                    28,
                    2,
                    2, 1, 0, 0, 0, 0, 0, 0,
                    1,
                });
            }

            [Fact]
            public void Writes_0_when_closed()
            {
                Bytes.Of(Written(false))[10].ShouldBe((byte)0);
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_the_name_and_whether_it_was_opened()
            {
                var r = Bytes.Back(Written(true));

                var box = ContainerLockMessage.Read(r, out bool open);

                box.ShouldBe(Bag);
                open.ShouldBeTrue();
            }

            [Fact]
            public void Takes_any_byte_but_0_as_opened()
            {
                var w = new PacketWriter(new RecordingLog());
                w.Begin(MsgType.ContainerLock);
                Bag.Write(w);
                w.W.Write((byte)7);

                ContainerLockMessage.Read(Bytes.Back(w), out bool open);

                open.ShouldBeTrue();
            }
        }
    }
}
