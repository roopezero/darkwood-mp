using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World;
using DarkwoodMP.World.Scripted;

namespace DarkwoodMP.Tests.World.Scripted
{
    public class EventFiredMessageTests
    {
        static readonly FiredEvent Fired = new FiredEvent
        {
            Kind = EventKind.WorldTrigger,
            Trigger = new ObjName(ObjKind.Saved, 2),
            Index = 3,
            Name = "ab",
        };

        static PacketWriter Written(FiredEvent fired)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.EventFired);
            EventFiredMessage.Write(w, fired);
            return w;
        }

        static byte[] BytesOf(PacketWriter w)
        {
            var bytes = new byte[w.Length];
            Array.Copy(w.Buffer, bytes, w.Length);
            return bytes;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_kind_trigger_index_and_names()
            {
                var w = Written(Fired);

                BytesOf(w).ShouldBe(new byte[]
                {
                    67,
                    3,
                    1,
                    2, 0, 0, 0, 0, 0, 0, 0,
                    3,
                    2, 0,
                    (byte)'a', (byte)'b',
                });
            }

            [Fact]
            public void Writes_a_world_event_with_no_trigger()
            {
                var w = Written(new FiredEvent
                {
                    Kind = EventKind.WorldEvent,
                    Name = "",
                });

                BytesOf(w).ShouldBe(new byte[]
                {
                    67,
                    2,
                    0,
                    0, 0, 0, 0, 0, 0, 0, 0,
                    0,
                    0, 0,
                });
            }

            [Fact]
            public void Writes_the_index_as_its_low_byte()
            {
                var w = Written(Fired with { Index = 257 });

                BytesOf(w)[11].ShouldBe((byte)1);
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(Fired);
                var r = new PacketReader(w.Buffer, w.Length);

                FiredEvent read = EventFiredMessage.Read(r);

                read.Kind.ShouldBe(EventKind.WorldTrigger);
                read.Trigger.ShouldBe(Fired.Trigger);
                read.Index.ShouldBe(3);
                read.Name.ShouldBe("ab");
            }
        }
    }
}
