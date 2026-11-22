using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World;
using DarkwoodMP.World.Dragging;

namespace DarkwoodMP.Tests.World.Dragging
{
    public class ObjMoveTests
    {
        static PacketWriter Written(Moved move)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.ObjMove);
            ObjMove.Write(w, move);
            return w;
        }

        static Moved Sample(bool last)
        {
            return new Moved
            {
                Name = new ObjName(ObjKind.Saved, 5),
                Pos = new Vec3(1f, -2f, 0.5f),
                Rot = new Rotation(0f, 0.5f, 0f, 1f),
                Last = last,
            };
        }

        public class Write
        {
            [Fact]
            public void Writes_the_name_place_turn_and_last_flag()
            {
                var w = Written(Sample(true));

                var bytes = new byte[w.Length];
                Array.Copy(w.Buffer, bytes, w.Length);
                bytes.ShouldBe(new byte[]
                {
                    33,
                    1,
                    5, 0, 0, 0, 0, 0, 0, 0,
                    0x00, 0x00, 0x80, 0x3F,
                    0x00, 0x00, 0x00, 0xC0,
                    0x00, 0x00, 0x00, 0x3F,
                    0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x3F,
                    0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x80, 0x3F,
                    1,
                });
            }

            [Fact]
            public void Writes_a_zero_flag_for_a_move_that_goes_on()
            {
                var w = Written(Sample(false));

                w.Buffer[w.Length - 1].ShouldBe((byte)0);
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(Sample(true));
                var r = new PacketReader(w.Buffer, w.Length);

                var move = ObjMove.Read(r);

                move.Name.ShouldBe(new ObjName(ObjKind.Saved, 5));
                move.Pos.Y.ShouldBe(-2f);
                move.Rot.Y.ShouldBe(0.5f);
                move.Rot.W.ShouldBe(1f);
                move.Last.ShouldBeTrue();
            }

            [Fact]
            public void Reads_only_the_lowest_bit_as_last()
            {
                var w = Written(Sample(false));
                w.Buffer[w.Length - 1] = 2;
                var r = new PacketReader(w.Buffer, w.Length);

                ObjMove.Read(r).Last.ShouldBeFalse();
            }
        }
    }
}
