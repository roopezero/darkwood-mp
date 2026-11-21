using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World;
using DarkwoodMP.World.Story;

namespace DarkwoodMP.Tests.World.Story
{
    public class StoryRequestTests
    {
        static PacketWriter Written(StoryAsk ask)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.StoryRequest);
            StoryRequest.Write(w, ask);
            return w;
        }

        static StoryAsk Chapter()
        {
            return new StoryAsk
            {
                Event = new ObjName(ObjKind.Saved, 7),
                Index = 3,
                Kind = StoryKind.Chapter,
                Chapter = 2,
            };
        }

        public class Write
        {
            [Fact]
            public void Writes_the_event_index_kind_and_chapter()
            {
                var w = Written(Chapter());

                var bytes = new byte[w.Length];
                Array.Copy(w.Buffer, bytes, w.Length);
                bytes.ShouldBe(new byte[]
                {
                    88,
                    1,
                    7, 0, 0, 0, 0, 0, 0, 0,
                    3,
                    1,
                    2, 0, 0, 0,
                });
            }

            [Fact]
            public void Writes_a_zero_name_for_a_step_with_no_event()
            {
                var w = Written(new StoryAsk { Kind = StoryKind.EndScreen });

                var bytes = new byte[w.Length];
                Array.Copy(w.Buffer, bytes, w.Length);
                bytes.ShouldBe(new byte[]
                {
                    88,
                    0,
                    0, 0, 0, 0, 0, 0, 0, 0,
                    0,
                    3,
                    0, 0, 0, 0,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(Chapter());
                var r = new PacketReader(w.Buffer, w.Length);

                var ask = StoryRequest.Read(r);

                ask.Event.ShouldBe(new ObjName(ObjKind.Saved, 7));
                ask.Index.ShouldBe((byte)3);
                ask.Kind.ShouldBe(StoryKind.Chapter);
                ask.Chapter.ShouldBe(2);
            }
        }
    }
}
