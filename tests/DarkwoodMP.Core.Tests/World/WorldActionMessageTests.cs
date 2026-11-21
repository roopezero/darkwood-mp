using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World;

namespace DarkwoodMP.Tests.World
{
    public class WorldActionMessageTests
    {
        static readonly WorldActionBody Trap = new WorldActionBody
        {
            Action = 21,
            Obj = new ObjName(ObjKind.Saved, 0x0102030405060708L),
            X = 1f,
            Y = -2f,
            Flags = WorldActionBody.First | WorldActionBody.ByPlayer,
            Text = "ab",
        };

        static PacketWriter Written(WorldActionBody body)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.WorldAction);
            WorldActionMessage.Write(w, body);
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
            public void Writes_action_name_numbers_flags_then_text()
            {
                Bytes(Written(Trap)).ShouldBe(new byte[]
                {
                    21,
                    21,
                    1, 8, 7, 6, 5, 4, 3, 2, 1,
                    0x00, 0x00, 0x80, 0x3F,
                    0x00, 0x00, 0x00, 0xC0,
                    5,
                    2, 0, (byte)'a', (byte)'b',
                });
            }

            [Fact]
            public void Writes_a_null_text_as_empty()
            {
                var body = Trap;
                body.Text = null;

                byte[] bytes = Bytes(Written(body));

                bytes.Length.ShouldBe(22);
                bytes[20].ShouldBe((byte)0);
                bytes[21].ShouldBe((byte)0);
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(Trap);
                var r = new PacketReader(w.Buffer, w.Length);

                var read = WorldActionMessage.Read(r);

                read.Action.ShouldBe(Trap.Action);
                read.Obj.ShouldBe(Trap.Obj);
                read.X.ShouldBe(1f);
                read.Y.ShouldBe(-2f);
                read.Flags.ShouldBe(Trap.Flags);
                read.Text.ShouldBe("ab");
            }
        }

        public class Has
        {
            [Fact]
            public void Says_whether_a_flag_is_set()
            {
                Trap.Has(WorldActionBody.ByPlayer).ShouldBeTrue();
                Trap.Has(WorldActionBody.Second).ShouldBeFalse();
            }
        }
    }
}
