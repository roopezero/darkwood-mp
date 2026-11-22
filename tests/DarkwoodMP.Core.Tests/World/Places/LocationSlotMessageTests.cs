using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World.Places;

namespace DarkwoodMP.Tests.World.Places
{
    public class LocationSlotMessageTests
    {
        static readonly LocationSlot Bunker = new LocationSlot
        {
            Name = "b",
            Spot = new Spot { Pos = new Vec3(1f, 0f, 2f), Turn = -90 },
        };

        static byte[] BytesOf(LocationSlot slot)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.LocationSlot);
            LocationSlotMessage.Write(w, slot);
            var bytes = new byte[w.Length];
            Array.Copy(w.Buffer, bytes, w.Length);
            return bytes;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_name_spot_and_turn()
            {
                BytesOf(Bunker).ShouldBe(new byte[]
                {
                    116,
                    1, 0, (byte)'b',
                    0x00, 0x00, 0x80, 0x3F,
                    0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x40,
                    0xA6, 0xFF,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var bytes = BytesOf(Bunker);

                var read = LocationSlotMessage.Read(
                    new PacketReader(bytes, bytes.Length));

                read.Name.ShouldBe("b");
                read.Spot.Pos.Z.ShouldBe(2f);
                read.Spot.Turn.ShouldBe(-90);
            }
        }
    }
}
