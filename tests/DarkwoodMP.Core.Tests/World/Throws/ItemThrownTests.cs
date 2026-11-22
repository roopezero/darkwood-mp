using System;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World.Throws;

namespace DarkwoodMP.Tests.World.Throws
{
    public class ItemThrownTests
    {
        /// A rock dropped by player 0x0102030405060708.
        static readonly ThrowReport Rock = new ThrowReport
        {
            By = 0x0102030405060708UL,
            Type = "rock",
            Pos = new Vec3(1f, 2f, -1f),
            Rot = new Quat(0f, 0.5f, 0f, 1f),
            Shot = new ThrowShot
            {
                Velocity = new Vec3(0f, 0f, 2f),
                Target = new Vec3(1f, 0f, 0f),
                Distance = -1f,
            },
        };

        static PacketWriter Written(ThrowReport report)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.ItemThrown);
            ItemThrown.Write(w, report);
            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_thrower_type_rotation_and_flight()
            {
                var w = Written(Rock);

                var bytes = new byte[w.Length];
                Array.Copy(w.Buffer, bytes, w.Length);
                bytes.ShouldBe(new byte[]
                {
                    35,
                    0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01,
                    4, 0, (byte)'r', (byte)'o', (byte)'c', (byte)'k',
                    0x00, 0x00, 0x80, 0x3F,
                    0x00, 0x00, 0x00, 0x40,
                    0x00, 0x00, 0x80, 0xBF,
                    0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x3F,
                    0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x80, 0x3F,
                    0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x40,
                    0x00, 0x00, 0x80, 0x3F,
                    0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x80, 0xBF,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(Rock);
                var r = new PacketReader(w.Buffer, w.Length);

                var report = ItemThrown.Read(r);

                report.By.ShouldBe(Rock.By);
                report.Type.ShouldBe("rock");
                report.Pos.Z.ShouldBe(-1f);
                report.Rot.Y.ShouldBe(0.5f);
                report.Rot.W.ShouldBe(1f);
                report.Shot.Velocity.Z.ShouldBe(2f);
                report.Shot.Target.X.ShouldBe(1f);
                report.Shot.Distance.ShouldBe(-1f);
            }
        }
    }
}
