using System;
using System.Collections.Generic;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World;

namespace DarkwoodMP.Tests.World
{
    public class HostIdsMessageTests
    {
        static readonly HostIdEntry Box = new HostIdEntry
        {
            Id = 2,
            Name = "ab",
            Pos = new Vec3(1f, -2f, 0.5f),
            Place = -1,
        };

        static readonly HostIdEntry Door = new HostIdEntry
        {
            Id = 0x0102030405060708L,
            Name = "Door1",
            Pos = new Vec3(120.5f, 3f, -7.25f),
            Place = 77,
        };

        static PacketWriter Written(byte flags, HostIdSlice ids)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.HostIds);
            HostIdsMessage.Write(w, flags, ids);
            return w;
        }

        static HostIdSlice All(params HostIdEntry[] ids) =>
            new HostIdSlice { List = ids, Start = 0, Count = ids.Length };

        public class Write
        {
            [Fact]
            public void Writes_flags_count_then_each_id_name_spot_and_place()
            {
                var w = Written(HostIdRules.MadeBefore, All(Box));

                var bytes = new byte[w.Length];
                Array.Copy(w.Buffer, bytes, w.Length);
                bytes.ShouldBe(new byte[]
                {
                    155,
                    1,
                    1, 0,
                    2, 0, 0, 0, 0, 0, 0, 0,
                    2, 0, (byte)'a', (byte)'b',
                    0x00, 0x00, 0x80, 0x3F,
                    0x00, 0x00, 0x00, 0xC0,
                    0x00, 0x00, 0x00, 0x3F,
                    0xFF, 0xFF, 0xFF, 0xFF,
                });
            }

            [Fact]
            public void Writes_only_the_slice_it_is_given()
            {
                var ids = new[] { Box, Door, Box };
                var w = Written(
                    0,
                    new HostIdSlice { List = ids, Start = 1, Count = 1 });
                var r = new PacketReader(w.Buffer, w.Length);

                var read = HostIdsMessage.Read(r, out _);

                read.Count.ShouldBe(1);
                read[0].Id.ShouldBe(Door.Id);
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_the_flags_and_the_ids_in_order()
            {
                var w = Written(0, All(Door, Box));
                var r = new PacketReader(w.Buffer, w.Length);

                List<HostIdEntry> read = HostIdsMessage.Read(r, out byte flags);

                flags.ShouldBe((byte)0);
                read.ShouldBe(new[] { Door, Box });
            }
        }
    }
}
