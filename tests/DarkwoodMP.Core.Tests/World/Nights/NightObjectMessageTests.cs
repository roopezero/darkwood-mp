using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.Tests.World.Inventories;
using DarkwoodMP.World;
using DarkwoodMP.World.Nights;

namespace DarkwoodMP.Tests.World.Nights
{
    public class NightObjectMessageTests
    {
        static readonly NightObjectBody Mushroom = new NightObjectBody
        {
            Kind = NightObjectRules.Made,
            Id = 4,
            Scenario = "S",
            Event = "E",
            Group = 2,
            Step = 5,
            Pos = new Vec3(1f, -2f, 0.5f),
            Rot = new Quat(0f, 1f, 0f, -2f),
            Parent = new ObjName(ObjKind.Saved, 9),
            Armed = NightObjectRules.Armed,
        };

        static PacketWriter Written(in NightObjectBody night)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.NightObject);
            NightObjectMessage.Write(w, night);
            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_each_field_in_order()
            {
                Bytes.Of(Written(Mushroom)).ShouldBe(new byte[]
                {
                    165,
                    1,
                    4, 0, 0, 0, 0, 0, 0, 0,
                    1, 0, (byte)'S',
                    1, 0, (byte)'E',
                    2,
                    5,
                    0x00, 0x00, 0x80, 0x3F,
                    0x00, 0x00, 0x00, 0xC0,
                    0x00, 0x00, 0x00, 0x3F,
                    0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x80, 0x3F,
                    0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0xC0,
                    1, 9, 0, 0, 0, 0, 0, 0, 0,
                    1,
                });
            }

            [Fact]
            public void Writes_only_the_kind_and_id_when_gone()
            {
                var gone = Mushroom with { Kind = NightObjectRules.Gone };

                Bytes.Of(Written(gone)).ShouldBe(new byte[]
                {
                    165,
                    2,
                    4, 0, 0, 0, 0, 0, 0, 0,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_every_field()
            {
                var read =
                    NightObjectMessage.Read(Bytes.Back(Written(Mushroom)));

                read.ShouldBe(Mushroom);
            }

            [Fact]
            public void Reads_only_the_kind_and_id_when_gone()
            {
                var gone = Mushroom with { Kind = NightObjectRules.Gone };

                var read = NightObjectMessage.Read(Bytes.Back(Written(gone)));

                read.ShouldBe(new NightObjectBody
                {
                    Kind = NightObjectRules.Gone,
                    Id = 4,
                });
            }
        }
    }
}
