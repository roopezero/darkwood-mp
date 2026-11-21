using System;
using DarkwoodMP.Combat.Effects;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;

namespace DarkwoodMP.Tests.Combat.Effects
{
    public class EnemyEffectMessageTests
    {
        static readonly EnemyEffect Scream = new EnemyEffect
        {
            Kind = 1,
            Name = "a",
            At = new Vec3(1f, 0f, 2f),
            RotY = 1f,
        };

        static byte[] BytesOf(EnemyEffect fx)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.EnemyEffect);
            EnemyEffectMessage.Write(w, fx);
            var bytes = new byte[w.Length];
            Array.Copy(w.Buffer, bytes, w.Length);
            return bytes;
        }

        static EnemyEffect RoundTrip(EnemyEffect fx)
        {
            var bytes = BytesOf(fx);
            return EnemyEffectMessage.Read(
                new PacketReader(bytes, bytes.Length));
        }

        public class Write
        {
            [Fact]
            public void Writes_a_prefab_with_its_rotation()
            {
                BytesOf(Scream).ShouldBe(new byte[]
                {
                    79,
                    1,
                    1, 0, (byte)'a',
                    0x00, 0x00, 0x80, 0x3F,
                    0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x40,
                    0x00, 0x00, 0x80, 0x3F,
                });
            }

            [Fact]
            public void Writes_a_pooled_effect_without_a_rotation()
            {
                BytesOf(Scream with { Kind = 2 }).ShouldBe(new byte[]
                {
                    79,
                    2,
                    1, 0, (byte)'a',
                    0x00, 0x00, 0x80, 0x3F,
                    0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x40,
                });
            }

            [Fact]
            public void Writes_an_emitter_as_its_id_and_toggle()
            {
                var emitter = new EnemyEffect
                {
                    Kind = 3,
                    Id = 5,
                    Toggle = 1,
                };

                BytesOf(emitter).ShouldBe(new byte[]
                {
                    79,
                    3,
                    5, 0, 0, 0,
                    1, 0, 0, 0,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_a_prefab()
            {
                var read = RoundTrip(Scream);

                read.Kind.ShouldBe((byte)1);
                read.Name.ShouldBe("a");
                read.At.Z.ShouldBe(2f);
                read.RotY.ShouldBe(1f);
            }

            [Fact]
            public void Reads_a_pooled_effect_with_no_rotation()
            {
                RoundTrip(Scream with { Kind = 2 }).RotY.ShouldBe(0f);
            }

            [Fact]
            public void Reads_back_an_emitter()
            {
                var emitter = new EnemyEffect
                {
                    Kind = 3,
                    Id = 5,
                    Toggle = 1,
                };

                var read = RoundTrip(emitter);

                read.Id.ShouldBe(5u);
                read.Toggle.ShouldBe(1);
            }
        }
    }
}
