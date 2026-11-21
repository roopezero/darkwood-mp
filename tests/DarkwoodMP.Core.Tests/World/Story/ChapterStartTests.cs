using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World.Story;

namespace DarkwoodMP.Tests.World.Story
{
    public class ChapterStartTests
    {
        static PacketWriter Written(int chapter)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.ChapterStart);
            ChapterStart.Write(w, chapter);
            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_chapter_after_the_type()
            {
                var w = Written(2);

                var bytes = new byte[w.Length];
                Array.Copy(w.Buffer, bytes, w.Length);
                bytes.ShouldBe(new byte[] { 86, 2, 0, 0, 0 });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(2);
                var r = new PacketReader(w.Buffer, w.Length);

                ChapterStart.Read(r).ShouldBe(2);
            }
        }
    }
}
