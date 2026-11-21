using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World;
using DarkwoodMP.World.Building;

namespace DarkwoodMP.Tests.World.Building
{
    public class ConstructTests
    {
        static readonly BuildOrder Feeder = new BuildOrder
        {
            Site = new ObjName(ObjKind.Spawned, 0x0102030405060708L),
            Option = 3,
        };

        static PacketWriter Written(BuildOrder order)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.Construct);
            Construct.Write(w, order);
            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_sites_kind_and_id_then_the_option()
            {
                var w = Written(Feeder);

                var bytes = new byte[w.Length];
                Array.Copy(w.Buffer, bytes, w.Length);
                bytes.ShouldBe(new byte[]
                {
                    66,
                    2,
                    0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01,
                    3, 0, 0, 0,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(Feeder);
                var r = new PacketReader(w.Buffer, w.Length);

                var order = Construct.Read(r);

                order.Site.ShouldBe(Feeder.Site);
                order.Option.ShouldBe(3);
            }

            [Fact]
            public void Reads_an_option_below_0_as_it_came()
            {
                var w = Written(Feeder with { Option = -1 });
                var r = new PacketReader(w.Buffer, w.Length);

                Construct.Read(r).Option.ShouldBe(-1);
            }
        }
    }
}
