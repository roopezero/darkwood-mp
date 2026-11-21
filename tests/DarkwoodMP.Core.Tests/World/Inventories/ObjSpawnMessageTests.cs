using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World.Inventories;

namespace DarkwoodMP.Tests.World.Inventories
{
    public class ObjSpawnMessageTests
    {
        static readonly SpawnedThing Trap = new SpawnedThing
        {
            Id = 4,
            Placed = true,
            Prefab = "T",
            Pos = new Vec3(1f, -2f, 0.5f),
            Rot = new Quat(0f, 1f, 0f, -2f),
            Contents = "{}",
            Experience = 7,
        };

        static PacketWriter Written(in SpawnedThing thing)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.ObjSpawn);
            ObjSpawnMessage.Write(w, thing);
            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_each_field_in_order()
            {
                Bytes.Of(Written(Trap)).ShouldBe(new byte[]
                {
                    23,
                    4, 0, 0, 0, 0, 0, 0, 0,
                    1,
                    1, 0, (byte)'T',
                    0x00, 0x00, 0x80, 0x3F,
                    0x00, 0x00, 0x00, 0xC0,
                    0x00, 0x00, 0x00, 0x3F,
                    0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x80, 0x3F,
                    0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0xC0,
                    2, 0, 0, 0, (byte)'{', (byte)'}',
                    7, 0, 0, 0,
                });
            }

            [Fact]
            public void Writes_flags_0_when_not_placed()
            {
                Bytes.Of(Written(Trap with { Placed = false }))[9]
                    .ShouldBe((byte)0);
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_every_field()
            {
                var read = ObjSpawnMessage.Read(Bytes.Back(Written(Trap)));

                read.ShouldBe(Trap);
            }

            [Fact]
            public void Takes_only_the_placed_bit_of_the_flags()
            {
                var w = Written(Trap with { Placed = false });
                w.Buffer[9] = 6;

                ObjSpawnMessage.Read(Bytes.Back(w)).Placed.ShouldBeFalse();
            }
        }
    }
}
