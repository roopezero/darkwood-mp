using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.Tests.World.Inventories;
using DarkwoodMP.World;
using DarkwoodMP.World.Hideout;

namespace DarkwoodMP.Tests.World.Hideout
{
    public class OvenLitMessageTests
    {
        static readonly LitOven Lit = new LitOven
        {
            Lit = new ObjName(ObjKind.Saved, 3),
            Out = new ObjName(ObjKind.Spawned, 0x0102030405060708L),
        };

        static PacketWriter Written(in LitOven oven)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.OvenLit);
            OvenLitMessage.Write(w, oven);
            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_lit_oven_then_the_one_that_goes_out()
            {
                Bytes.Of(Written(Lit)).ShouldBe(new byte[]
                {
                    122,
                    1, 3, 0, 0, 0, 0, 0, 0, 0,
                    2, 8, 7, 6, 5, 4, 3, 2, 1,
                });
            }

            [Fact]
            public void Writes_an_empty_ref_for_no_oven_going_out()
            {
                var w = Written(Lit with { Out = default });

                Bytes.Of(w)[10..].ShouldBe(new byte[9]);
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_both_ovens()
            {
                OvenLitMessage.Read(Bytes.Back(Written(Lit))).ShouldBe(Lit);
            }
        }
    }
}
