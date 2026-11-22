using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World;
using DarkwoodMP.World.Eggs;

namespace DarkwoodMP.Tests.World.Eggs
{
    public class EggDestroyedTests
    {
        static readonly EggBreak Egg = new EggBreak
        {
            Egg = new ObjName(ObjKind.Saved, 2),
            At = new Vec2(1f, -2f),
            Name = "ab",
        };

        static PacketWriter Written(EggBreak egg)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.EggDestroyed);
            EggDestroyed.Write(w, egg);
            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_egg_where_it_was_then_its_name()
            {
                var w = Written(Egg);

                var bytes = new byte[w.Length];
                Array.Copy(w.Buffer, bytes, w.Length);
                bytes.ShouldBe(new byte[]
                {
                    0xD0,
                    1,
                    2, 0, 0, 0, 0, 0, 0, 0,
                    0x00, 0x00, 0x80, 0x3F,
                    0x00, 0x00, 0x00, 0xC0,
                    2, 0, (byte)'a', (byte)'b',
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(Egg);
                var r = new PacketReader(w.Buffer, w.Length);

                EggBreak read = EggDestroyed.Read(r);

                read.Egg.ShouldBe(Egg.Egg);
                read.At.ShouldBe(Egg.At);
                read.Name.ShouldBe("ab");
            }
        }
    }
}
