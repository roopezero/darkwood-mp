using System;
using DarkwoodMP.Combat.Landings;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;

namespace DarkwoodMP.Tests.Combat.Landings
{
    public class EnemySensorThrownMessageTests
    {
        static readonly EnemySensorThrow Web = new EnemySensorThrow
        {
            Throw = new EnemyThrow
            {
                By = 3,
                Name = "",
                At = new Vec3(0f, 0f, 0f),
                Yaw = 0f,
                Velocity = new Vec3(0f, 0f, 0f),
            },
            Land = new Vec2(1f, 2f),
        };

        static byte[] BytesOf(EnemySensorThrow thrown)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.EnemySensorThrown);
            EnemySensorThrownMessage.Write(w, thrown);
            var bytes = new byte[w.Length];
            Array.Copy(w.Buffer, bytes, w.Length);
            return bytes;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_throw_then_where_it_lands()
            {
                var bytes = BytesOf(Web);

                bytes.Length.ShouldBe(1 + 4 + 2 + 12 + 4 + 12 + 8);
                bytes[0].ShouldBe((byte)90);
                bytes[1].ShouldBe((byte)3);
                bytes[^8..].ShouldBe(new byte[]
                {
                    0x00, 0x00, 0x80, 0x3F,
                    0x00, 0x00, 0x00, 0x40,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var bytes = BytesOf(Web);
                var r = new PacketReader(bytes, bytes.Length);

                var read = EnemySensorThrownMessage.Read(r);

                read.Throw.By.ShouldBe(3u);
                read.Throw.Name.ShouldBe("");
                read.Land.X.ShouldBe(1f);
                read.Land.Z.ShouldBe(2f);
            }
        }
    }
}
