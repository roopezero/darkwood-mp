using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World;
using DarkwoodMP.World.Fire;

namespace DarkwoodMP.Tests.World.Fire
{
    public class BurningTests
    {
        static readonly FireReport Door = new FireReport
        {
            Thing = new ObjName(ObjKind.Saved, 2),
            Kind = FireKind.Door,
            Time = 1f,
            On = true,
        };

        static PacketWriter Written(FireReport fire)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.Burning);
            Burning.Write(w, fire);
            return w;
        }

        static byte[] Bytes(PacketWriter w)
        {
            var bytes = new byte[w.Length];
            Array.Copy(w.Buffer, bytes, w.Length);
            return bytes;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_thing_what_it_is_its_time_then_on()
            {
                Bytes(Written(Door)).ShouldBe(new byte[]
                {
                    36,
                    1,
                    2, 0, 0, 0, 0, 0, 0, 0,
                    2,
                    0x00, 0x00, 0x80, 0x3F,
                    1,
                });
            }

            [Fact]
            public void Writes_off_as_a_zero()
            {
                var off = Door with { On = false };

                Bytes(Written(off))[^1].ShouldBe((byte)0);
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(Door);
                var r = new PacketReader(w.Buffer, w.Length);

                FireReport read = Burning.Read(r);

                read.Thing.ShouldBe(Door.Thing);
                read.Kind.ShouldBe(FireKind.Door);
                read.Time.ShouldBe(1f);
                read.On.ShouldBeTrue();
            }

            [Fact]
            public void Reads_any_nonzero_on_byte_as_on()
            {
                var w = Written(Door with { On = false });
                w.Buffer[w.Length - 1] = 7;
                var r = new PacketReader(w.Buffer, w.Length);

                Burning.Read(r).On.ShouldBeTrue();
            }

            [Fact]
            public void Reads_an_unknown_kind_as_it_was_sent()
            {
                var w = Written(Door);
                w.Buffer[10] = 9;
                var r = new PacketReader(w.Buffer, w.Length);

                ((byte)Burning.Read(r).Kind).ShouldBe((byte)9);
            }
        }
    }
}
