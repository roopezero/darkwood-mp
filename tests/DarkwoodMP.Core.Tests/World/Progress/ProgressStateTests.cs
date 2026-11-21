using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World.Progress;

namespace DarkwoodMP.Tests.World.Progress
{
    public class ProgressStateTests
    {
        static PacketWriter Written(int exp, int level)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.ProgressState);
            ProgressState.Write(
                w,
                new ProgressNumbers { Exp = exp, Level = level });
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
            public void Writes_the_essence_as_i32_then_the_level_as_u8()
            {
                var w = Written(0x01020304, 6);

                Bytes(w).ShouldBe(new byte[]
                {
                    180,
                    0x04, 0x03, 0x02, 0x01,
                    6,
                });
            }

            [Fact]
            public void Clamps_the_level_into_a_byte()
            {
                Bytes(Written(0, 300))[5].ShouldBe((byte)255);
                Bytes(Written(0, -1))[5].ShouldBe((byte)0);
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(5400, 4);
                var r = new PacketReader(w.Buffer, w.Length);

                var shared = ProgressState.Read(r);

                shared.Exp.ShouldBe(5400);
                shared.Level.ShouldBe(4);
            }
        }
    }
}
