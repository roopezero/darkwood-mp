using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World;
using DarkwoodMP.World.Inventories;

namespace DarkwoodMP.Tests.World.Inventories
{
    public class ContainerSlotRefusedMessageTests
    {
        static readonly ObjName Chest = new ObjName(ObjKind.Saved, 5);

        static readonly RefusedSlot Slot = new RefusedSlot
        {
            Slot = 0x0102,
            Held = "h",
            Old = "",
            Tried = "{}",
        };

        static PacketWriter Written()
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.ContainerSlotRefused);
            ContainerSlotRefusedMessage.Write(w, Chest, Slot);
            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_name_the_slot_then_the_three_strings()
            {
                Bytes.Of(Written()).ShouldBe(new byte[]
                {
                    38,
                    1,
                    5, 0, 0, 0, 0, 0, 0, 0,
                    2, 1,
                    1, 0, (byte)'h',
                    0, 0,
                    2, 0, (byte)'{', (byte)'}',
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_the_name_and_the_refused_change()
            {
                var r = Bytes.Back(Written());

                var box = ContainerSlotRefusedMessage.Read(
                    r, out RefusedSlot slot);

                box.ShouldBe(Chest);
                slot.ShouldBe(Slot);
            }
        }
    }
}
