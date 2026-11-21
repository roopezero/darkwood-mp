using System;
using DarkwoodMP.Combat.Enemies;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;

namespace DarkwoodMP.Tests.Combat.Enemies
{
    public class EnemyBloodMessageTests
    {
        static readonly EnemyBlood Splat = new EnemyBlood
        {
            Id = 5,
            RotY = 2f,
            Water = true,
        };

        static PacketWriter Written(EnemyBlood b)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.EnemyBlood);
            EnemyBloodMessage.Write(w, b);
            return w;
        }

        static byte[] BytesOf(PacketWriter w)
        {
            var bytes = new byte[w.Length];
            Array.Copy(w.Buffer, bytes, w.Length);
            return bytes;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_id_rotation_and_water()
            {
                BytesOf(Written(Splat)).ShouldBe(new byte[]
                {
                    30,
                    5, 0, 0, 0,
                    0x00, 0x00, 0x00, 0x40,
                    1,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(Splat with { Water = false });
                var b = EnemyBloodMessage.Read(
                    new PacketReader(w.Buffer, w.Length));

                b.Id.ShouldBe(5u);
                b.RotY.ShouldBe(2f);
                b.Water.ShouldBeFalse();
            }
        }
    }
}
