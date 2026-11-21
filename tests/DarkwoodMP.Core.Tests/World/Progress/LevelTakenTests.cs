using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World.Progress;

namespace DarkwoodMP.Tests.World.Progress
{
    public class LevelTakenTests
    {
        static PacketWriter Written(int level)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.LevelTaken);
            LevelTaken.Write(w, level);
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
            public void Writes_the_level_as_one_byte()
            {
                Bytes(Written(3)).ShouldBe(new byte[] { 182, 3 });
            }

            [Fact]
            public void Clamps_the_level_into_a_byte()
            {
                Bytes(Written(256)).ShouldBe(new byte[] { 182, 255 });
                Bytes(Written(-2)).ShouldBe(new byte[] { 182, 0 });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(7);
                var r = new PacketReader(w.Buffer, w.Length);

                LevelTaken.Read(r).ShouldBe(7);
            }
        }
    }
}
