using System.Collections.Generic;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World;
using DarkwoodMP.World.Inventories;

namespace DarkwoodMP.Tests.World.Inventories
{
    public class ContainerSlotsMessageTests
    {
        static readonly ObjName Storage = new ObjName(ObjKind.Saved, 7);

        static readonly SlotChange Put = new SlotChange
        {
            Slot = 0x0102,
            Old = "",
            New = "{}",
        };

        static readonly SlotChange Took = new SlotChange
        {
            Slot = 3,
            Old = "a",
            New = "",
        };

        static PacketWriter Written(params SlotChange[] changes)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.ContainerSlots);
            ContainerSlotsMessage.Write(w, Storage, changes);
            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_name_the_count_then_each_change()
            {
                Bytes.Of(Written(Put, Took)).ShouldBe(new byte[]
                {
                    37,
                    1,
                    7, 0, 0, 0, 0, 0, 0, 0,
                    2,
                    2, 1,
                    0, 0,
                    2, 0, (byte)'{', (byte)'}',
                    3, 0,
                    1, 0, (byte)'a',
                    0, 0,
                });
            }

            [Fact]
            public void Writes_a_count_of_0_with_no_changes()
            {
                Bytes.Of(Written()).ShouldBe(new byte[]
                {
                    37,
                    1,
                    7, 0, 0, 0, 0, 0, 0, 0,
                    0,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_the_name_and_the_changes_in_order()
            {
                var r = Bytes.Back(Written(Put, Took));

                var box = ContainerSlotsMessage.Read(
                    r, out List<SlotChange> changes);

                box.ShouldBe(Storage);
                changes.ShouldBe(new[] { Put, Took });
            }
        }
    }
}
