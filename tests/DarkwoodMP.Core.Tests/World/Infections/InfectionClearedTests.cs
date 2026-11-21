using System;
using System.Collections.Generic;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World;
using DarkwoodMP.World.Infections;

namespace DarkwoodMP.Tests.World.Infections
{
    public class InfectionClearedTests
    {
        static readonly ClearedPatch[] Patches =
        {
            new ClearedPatch
            {
                Patch = new ObjName(ObjKind.Saved, 2),
                At = new Vec2(1f, -2f),
            },
            new ClearedPatch
            {
                Patch = new ObjName(ObjKind.Saved, 3),
                At = new Vec2(0f, 1f),
            },
        };

        static PacketWriter Written(IReadOnlyList<ClearedPatch> patches)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.InfectionCleared);
            InfectionCleared.Write(w, patches);
            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_count_then_each_patch_where_it_was()
            {
                var w = Written(Patches);

                var bytes = new byte[w.Length];
                Array.Copy(w.Buffer, bytes, w.Length);
                bytes.ShouldBe(new byte[]
                {
                    0xD1,
                    2, 0,
                    1,
                    2, 0, 0, 0, 0, 0, 0, 0,
                    0x00, 0x00, 0x80, 0x3F,
                    0x00, 0x00, 0x00, 0xC0,
                    1,
                    3, 0, 0, 0, 0, 0, 0, 0,
                    0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x80, 0x3F,
                });
            }

            [Fact]
            public void Writes_only_the_count_of_none()
            {
                var w = Written(new ClearedPatch[0]);

                var bytes = new byte[w.Length];
                Array.Copy(w.Buffer, bytes, w.Length);
                bytes.ShouldBe(new byte[] { 0xD1, 0, 0 });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(Patches);
                var r = new PacketReader(w.Buffer, w.Length);

                List<ClearedPatch> read = InfectionCleared.Read(r);

                read.Count.ShouldBe(2);
                read[0].Patch.ShouldBe(Patches[0].Patch);
                read[0].At.ShouldBe(Patches[0].At);
                read[1].Patch.ShouldBe(Patches[1].Patch);
                read[1].At.ShouldBe(Patches[1].At);
            }
        }
    }
}
