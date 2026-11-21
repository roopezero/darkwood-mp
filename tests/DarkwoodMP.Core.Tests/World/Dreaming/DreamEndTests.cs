using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World.Dreaming;

namespace DarkwoodMP.Tests.World.Dreaming
{
    public class DreamEndTests
    {
        static PacketWriter Written(string outcome, string dream)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.DreamEnd);
            var end = new DreamEnded { Outcome = outcome, Dream = dream };
            DreamEnd.Write(w, end);
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
            public void Writes_the_outcome_then_the_dream()
            {
                var w = Written("x", "ab");

                Bytes(w).ShouldBe(new byte[] { 185, 1, 0, 120, 2, 0, 97, 98 });
            }

            [Fact]
            public void Writes_nulls_as_empty_strings()
            {
                var w = Written(null, null);

                Bytes(w).ShouldBe(new byte[] { 185, 0, 0, 0, 0 });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written("died", "dream_03");
                var r = new PacketReader(w.Buffer, w.Length);

                DreamEnded end = DreamEnd.Read(r);

                end.Outcome.ShouldBe("died");
                end.Dream.ShouldBe("dream_03");
            }
        }
    }
}
