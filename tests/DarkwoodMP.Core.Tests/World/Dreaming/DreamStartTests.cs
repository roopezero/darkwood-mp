using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World.Dreaming;

namespace DarkwoodMP.Tests.World.Dreaming
{
    public class DreamStartTests
    {
        static PacketWriter Written(string dream)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.DreamStart);
            DreamStart.Write(w, dream);
            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_dream_after_the_type()
            {
                var w = Written("ab");

                var bytes = new byte[w.Length];
                Array.Copy(w.Buffer, bytes, w.Length);
                bytes.ShouldBe(new byte[] { 184, 2, 0, 97, 98 });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written("dream_03");
                var r = new PacketReader(w.Buffer, w.Length);

                DreamStart.Read(r).ShouldBe("dream_03");
            }
        }
    }
}
