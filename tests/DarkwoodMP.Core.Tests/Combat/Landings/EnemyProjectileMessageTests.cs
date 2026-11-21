using System;
using DarkwoodMP.Combat.Landings;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;

namespace DarkwoodMP.Tests.Combat.Landings
{
    public class EnemyProjectileMessageTests
    {
        static readonly EnemyThrow Spit = new EnemyThrow
        {
            By = 3,
            Name = "p",
            At = new Vec3(1f, 0f, 2f),
            Yaw = 0.5f,
            Velocity = new Vec3(-1f, 0f, 1f),
        };

        static byte[] BytesOf(EnemyThrow thrown)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.EnemyProjectile);
            EnemyProjectileMessage.Write(w, thrown);
            var bytes = new byte[w.Length];
            Array.Copy(w.Buffer, bytes, w.Length);
            return bytes;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_thrower_name_position_yaw_and_velocity()
            {
                BytesOf(Spit).ShouldBe(new byte[]
                {
                    236,
                    3, 0, 0, 0,
                    1, 0, (byte)'p',
                    0x00, 0x00, 0x80, 0x3F,
                    0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x40,
                    0x00, 0x00, 0x00, 0x3F,
                    0x00, 0x00, 0x80, 0xBF,
                    0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x80, 0x3F,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var bytes = BytesOf(Spit);
                var r = new PacketReader(bytes, bytes.Length);

                var read = EnemyProjectileMessage.Read(r);

                read.By.ShouldBe(3u);
                read.Name.ShouldBe("p");
                read.At.Z.ShouldBe(2f);
                read.Yaw.ShouldBe(0.5f);
                read.Velocity.X.ShouldBe(-1f);
            }
        }
    }
}
