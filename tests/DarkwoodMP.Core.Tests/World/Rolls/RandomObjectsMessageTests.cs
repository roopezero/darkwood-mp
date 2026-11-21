using System.Collections.Generic;
using DarkwoodMP.Net;
using DarkwoodMP.World.Rolls;

namespace DarkwoodMP.Tests.World.Rolls
{
    public class RandomObjectsMessageTests
    {
        static readonly DawnObject Crate = new DawnObject
        {
            Id = 5,
            Chunk = 1,
            Object = 258,
            Local = new Vec3(1f, 0f, 2f),
            Contents = "{}",
        };

        static readonly NightMushroom Mushroom = new NightMushroom
        {
            Id = 7,
            Prefab = "m",
            Pos = new Vec3(0f, 1f, 0f),
        };

        public class WriteDawn
        {
            [Fact]
            public void Writes_the_kind_count_and_each_object()
            {
                var w = RollBytes.Begin(MsgType.RandomObjects);
                RandomObjectsMessage.WriteDawn(
                    w, new List<DawnObject> { Crate });

                RollBytes.Of(w).ShouldBe(new byte[]
                {
                    64,
                    1,
                    1, 0,
                    5, 0, 0, 0, 0, 0, 0, 0,
                    1, 0,
                    2, 1,
                    0x00, 0x00, 0x80, 0x3F,
                    0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x40,
                    2, 0, (byte)'{', (byte)'}',
                });
            }

            [Fact]
            public void Writes_none()
            {
                var w = RollBytes.Begin(MsgType.RandomObjects);
                RandomObjectsMessage.WriteDawn(w, new List<DawnObject>());

                RollBytes.Of(w).ShouldBe(new byte[] { 64, 1, 0, 0 });
            }
        }

        public class WriteNight
        {
            [Fact]
            public void Writes_the_kind_id_prefab_and_place()
            {
                var w = RollBytes.Begin(MsgType.RandomObjects);
                RandomObjectsMessage.WriteNight(w, Mushroom);

                RollBytes.Of(w).ShouldBe(new byte[]
                {
                    64,
                    2,
                    7, 0, 0, 0, 0, 0, 0, 0,
                    1, 0, (byte)'m',
                    0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x80, 0x3F,
                    0x00, 0x00, 0x00, 0x00,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_the_dawns_objects()
            {
                var w = RollBytes.Begin(MsgType.RandomObjects);
                RandomObjectsMessage.WriteDawn(
                    w, new List<DawnObject> { Crate, Crate });

                var body = RandomObjectsMessage.Read(RollBytes.Reader(w));

                body.Kind.ShouldBe(RandomObjectsMessage.Dawn);
                body.Dawn.Count.ShouldBe(2);
                body.Dawn[1].Id.ShouldBe(5L);
                body.Dawn[1].Chunk.ShouldBe(1);
                body.Dawn[1].Object.ShouldBe(258);
                body.Dawn[1].Local.ShouldBe(new Vec3(1f, 0f, 2f));
                body.Dawn[1].Contents.ShouldBe("{}");
            }

            [Fact]
            public void Reads_back_a_night_mushroom()
            {
                var w = RollBytes.Begin(MsgType.RandomObjects);
                RandomObjectsMessage.WriteNight(w, Mushroom);

                var body = RandomObjectsMessage.Read(RollBytes.Reader(w));

                body.Kind.ShouldBe(RandomObjectsMessage.Night);
                body.Night.Id.ShouldBe(7L);
                body.Night.Prefab.ShouldBe("m");
                body.Night.Pos.ShouldBe(new Vec3(0f, 1f, 0f));
            }

            [Fact]
            public void Keeps_an_unknown_kind()
            {
                var w = RollBytes.Begin(MsgType.RandomObjects);
                w.W.Write((byte)9);

                RandomObjectsMessage.Read(RollBytes.Reader(w)).Kind
                    .ShouldBe((byte)9);
            }
        }
    }
}
