using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World;
using DarkwoodMP.World.Doors;

namespace DarkwoodMP.Tests.World.Doors
{
    public class DoorLeafMessageTests
    {
        static readonly ObjName Door = new ObjName(ObjKind.Saved, 0x0102);

        static PacketWriter Written(float angle)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.DoorLeaf);
            DoorLeafMessage.Write(w, Door, angle);
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
            public void Writes_the_door_then_the_angle()
            {
                var w = Written(-2f);

                BytesOf(w).ShouldBe(new byte[]
                {
                    100,
                    1,
                    0x02, 0x01, 0, 0, 0, 0, 0, 0,
                    0, 0, 0, 0xC0,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(37.5f);
                var r = new PacketReader(w.Buffer, w.Length);

                var door = DoorLeafMessage.Read(r, out float angle);

                r.Type.ShouldBe(MsgType.DoorLeaf);
                door.ShouldBe(Door);
                angle.ShouldBe(37.5f);
            }

            [Fact]
            public void Keeps_an_angle_that_is_not_a_number()
            {
                var w = Written(float.NaN);
                var r = new PacketReader(w.Buffer, w.Length);

                DoorLeafMessage.Read(r, out float angle);

                float.IsNaN(angle).ShouldBeTrue();
            }
        }
    }
}
