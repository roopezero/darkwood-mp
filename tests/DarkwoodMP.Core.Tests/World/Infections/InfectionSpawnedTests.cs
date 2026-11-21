using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World;
using DarkwoodMP.World.Infections;

namespace DarkwoodMP.Tests.World.Infections
{
    public class InfectionSpawnedTests
    {
        static readonly SpawnedPatch Patch = new SpawnedPatch
        {
            Id = 5,
            At = new Vec3(1f, -2f, 0f),
            Parent = new ObjName(ObjKind.Saved, 2),
        };

        static PacketWriter Written(SpawnedPatch patch)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.InfectionSpawned);
            InfectionSpawned.Write(w, patch);
            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_id_where_it_is_then_its_parent()
            {
                var w = Written(Patch);

                var bytes = new byte[w.Length];
                Array.Copy(w.Buffer, bytes, w.Length);
                bytes.ShouldBe(new byte[]
                {
                    0xD2,
                    5, 0, 0, 0, 0, 0, 0, 0,
                    0x00, 0x00, 0x80, 0x3F,
                    0x00, 0x00, 0x00, 0xC0,
                    0x00, 0x00, 0x00, 0x00,
                    1,
                    2, 0, 0, 0, 0, 0, 0, 0,
                });
            }

            [Fact]
            public void Writes_no_parent_as_kind_0()
            {
                var w = Written(Patch with { Parent = default });

                var bytes = new byte[w.Length];
                Array.Copy(w.Buffer, bytes, w.Length);
                bytes[21].ShouldBe((byte)0);
                w.Length.ShouldBe(30);
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(Patch);
                var r = new PacketReader(w.Buffer, w.Length);

                SpawnedPatch read = InfectionSpawned.Read(r);

                read.Id.ShouldBe(5L);
                read.At.X.ShouldBe(1f);
                read.At.Y.ShouldBe(-2f);
                read.At.Z.ShouldBe(0f);
                read.Parent.ShouldBe(Patch.Parent);
            }
        }
    }
}
