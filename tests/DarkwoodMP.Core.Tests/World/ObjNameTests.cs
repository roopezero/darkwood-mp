using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World;

namespace DarkwoodMP.Tests.World
{
    public class ObjNameTests
    {
        static PacketWriter Written(ObjName name)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.ObjState);
            name.Write(w);
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
            public void Writes_the_kind_byte_then_the_id_little_endian()
            {
                var w = Written(new ObjName(ObjKind.Placed, 0x0102030405L));

                BytesOf(w).ShouldBe(new byte[]
                {
                    (byte)MsgType.ObjState,
                    4,
                    0x05, 0x04, 0x03, 0x02, 0x01, 0x00, 0x00, 0x00,
                });
            }

            [Fact]
            public void Writes_nothing_as_kind_zero_and_id_zero()
            {
                var w = Written(default);

                BytesOf(w).ShouldBe(new byte[]
                {
                    (byte)MsgType.ObjState,
                    0,
                    0, 0, 0, 0, 0, 0, 0, 0,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var name = new ObjName(ObjKind.Spawned, -7L);
                var w = Written(name);
                var r = new PacketReader(w.Buffer, w.Length);

                ObjName.Read(r).ShouldBe(name);
            }

            [Fact]
            public void Keeps_an_unknown_kind_byte()
            {
                var w = Written(new ObjName((ObjKind)9, 3L));
                var r = new PacketReader(w.Buffer, w.Length);

                ((byte)ObjName.Read(r).Kind).ShouldBe((byte)9);
            }
        }
    }
}
