using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World;
using DarkwoodMP.World.Dragging;

namespace DarkwoodMP.Tests.World.Dragging
{
    public class DragClaimTests
    {
        static PacketWriter Written(ObjName thing)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.DragClaim);
            DragClaim.Write(w, thing);
            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_name_of_the_thing()
            {
                var w = Written(new ObjName(ObjKind.Spawned, 258));

                var bytes = new byte[w.Length];
                Array.Copy(w.Buffer, bytes, w.Length);
                bytes.ShouldBe(new byte[]
                {
                    48,
                    2,
                    2, 1, 0, 0, 0, 0, 0, 0,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(new ObjName(ObjKind.Spawned, 258));
                var r = new PacketReader(w.Buffer, w.Length);

                DragClaim.Read(r).ShouldBe(new ObjName(ObjKind.Spawned, 258));
            }
        }
    }
}
