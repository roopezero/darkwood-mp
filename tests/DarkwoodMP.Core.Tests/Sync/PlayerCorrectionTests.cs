using System;
using DarkwoodMP.Net;
using DarkwoodMP.Sync;
using DarkwoodMP.Tests.Net;

namespace DarkwoodMP.Tests.Sync
{
    public class PlayerCorrectionTests
    {
        static PacketWriter Written(Vec3 accepted)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.PlayerCorrection);
            PlayerCorrection.Write(w, accepted);
            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_position_as_three_little_endian_floats()
            {
                var w = Written(new Vec3(1f, -2f, 0.5f));

                var bytes = new byte[w.Length];
                Array.Copy(w.Buffer, bytes, w.Length);
                bytes.ShouldBe(new byte[]
                {
                    43,
                    0x00, 0x00, 0x80, 0x3F,
                    0x00, 0x00, 0x00, 0xC0,
                    0x00, 0x00, 0x00, 0x3F,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(new Vec3(120.5f, 3f, -7.25f));
                var r = new PacketReader(w.Buffer, w.Length);

                var at = PlayerCorrection.Read(r);

                at.X.ShouldBe(120.5f);
                at.Y.ShouldBe(3f);
                at.Z.ShouldBe(-7.25f);
            }
        }
    }
}
