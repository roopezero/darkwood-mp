using System;
using DarkwoodMP.Combat.Damage;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;

namespace DarkwoodMP.Tests.Combat.Damage
{
    public class EnemyExplosionMessageTests
    {
        static readonly EnemyExplosion Bomb = new EnemyExplosion
        {
            At = new Vec3(1f, 0f, 2f),
            Sound = "b",
            Effect = "",
        };

        static byte[] BytesOf(EnemyExplosion blast)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.EnemyExplosion);
            EnemyExplosionMessage.Write(w, blast);
            var bytes = new byte[w.Length];
            Array.Copy(w.Buffer, bytes, w.Length);
            return bytes;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_position_sound_and_effect()
            {
                BytesOf(Bomb).ShouldBe(new byte[]
                {
                    47,
                    0x00, 0x00, 0x80, 0x3F,
                    0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x40,
                    1, 0, (byte)'b',
                    0, 0,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var bytes = BytesOf(Bomb);

                var read = EnemyExplosionMessage.Read(
                    new PacketReader(bytes, bytes.Length));

                read.At.X.ShouldBe(1f);
                read.Sound.ShouldBe("b");
                read.Effect.ShouldBe("");
            }
        }
    }
}
