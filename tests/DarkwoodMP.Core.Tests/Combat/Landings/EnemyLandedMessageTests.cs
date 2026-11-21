using System;
using DarkwoodMP.Combat.Landings;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;

namespace DarkwoodMP.Tests.Combat.Landings
{
    public class EnemyLandedMessageTests
    {
        static readonly EnemyLanding Spawner = new EnemyLanding
        {
            Id = 2,
            By = 3,
            Name = "f",
            At = new Vec3(1f, 0f, 2f),
            Yaw = -1f,
        };

        static byte[] BytesOf(EnemyLanding landed)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.EnemyLanded);
            EnemyLandedMessage.Write(w, landed);
            var bytes = new byte[w.Length];
            Array.Copy(w.Buffer, bytes, w.Length);
            return bytes;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_id_thrower_name_position_and_yaw()
            {
                BytesOf(Spawner).ShouldBe(new byte[]
                {
                    235,
                    2, 0, 0, 0, 0, 0, 0, 0,
                    3, 0, 0, 0,
                    1, 0, (byte)'f',
                    0x00, 0x00, 0x80, 0x3F,
                    0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x40,
                    0x00, 0x00, 0x80, 0xBF,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var bytes = BytesOf(Spawner);
                var r = new PacketReader(bytes, bytes.Length);

                var read = EnemyLandedMessage.Read(r);

                read.Id.ShouldBe(2L);
                read.By.ShouldBe(3u);
                read.Name.ShouldBe("f");
                read.At.X.ShouldBe(1f);
                read.Yaw.ShouldBe(-1f);
            }
        }
    }
}
