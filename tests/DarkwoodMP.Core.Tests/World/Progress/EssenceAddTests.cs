using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World.Progress;

namespace DarkwoodMP.Tests.World.Progress
{
    public class EssenceAddTests
    {
        static PacketWriter Written(int amount)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.EssenceAdd);
            EssenceAdd.Write(w, amount);
            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_amount_as_a_little_endian_i32()
            {
                var w = Written(300);

                var bytes = new byte[w.Length];
                Array.Copy(w.Buffer, bytes, w.Length);
                bytes.ShouldBe(new byte[] { 181, 0x2C, 0x01, 0x00, 0x00 });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(-7);
                var r = new PacketReader(w.Buffer, w.Length);

                EssenceAdd.Read(r).ShouldBe(-7);
            }
        }
    }
}
