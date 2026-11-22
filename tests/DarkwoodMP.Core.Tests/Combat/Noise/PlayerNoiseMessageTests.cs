using System;
using DarkwoodMP.Combat.Noise;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;

namespace DarkwoodMP.Tests.Combat.Noise
{
    public class PlayerNoiseMessageTests
    {
        static readonly SentNoise Shot = new SentNoise
        {
            At = new Vec3(1f, 0f, -2f),
            Range = 300f,
            Volume = 1.5f,
            Dangerous = true,
            Gunshot = true,
        };

        static byte[] BytesOf(in SentNoise noise)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.PlayerNoise);
            PlayerNoiseMessage.Write(w, noise);
            var bytes = new byte[w.Length];
            Array.Copy(w.Buffer, bytes, w.Length);
            return bytes;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_place_range_volume_and_flags()
            {
                BytesOf(Shot).ShouldBe(new byte[]
                {
                    19,
                    0, 0, 128, 63, 0, 0, 0, 0, 0, 0, 0, 192,
                    0, 0, 150, 67,
                    0, 0, 192, 63,
                    3,
                });
            }

            [Fact]
            public void Writes_a_quiet_step_with_no_flags()
            {
                var step = new SentNoise { Range = 1f, Volume = 1f };

                byte[] bytes = BytesOf(step);

                bytes[bytes.Length - 1].ShouldBe((byte)0);
            }

            [Fact]
            public void Writes_a_dangerous_sound_as_flag_1()
            {
                var noise = new SentNoise { Dangerous = true };

                byte[] bytes = BytesOf(noise);

                bytes[bytes.Length - 1].ShouldBe((byte)1);
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                byte[] bytes = BytesOf(Shot);

                SentNoise read = PlayerNoiseMessage.Read(
                    new PacketReader(bytes, bytes.Length));

                read.At.ShouldBe(Shot.At);
                read.Range.ShouldBe(300f);
                read.Volume.ShouldBe(1.5f);
                read.Dangerous.ShouldBeTrue();
                read.Gunshot.ShouldBeTrue();
            }

            [Fact]
            public void Reads_flag_2_alone_as_a_gunshot_only()
            {
                var noise = new SentNoise { Gunshot = true };
                byte[] bytes = BytesOf(noise);

                SentNoise read = PlayerNoiseMessage.Read(
                    new PacketReader(bytes, bytes.Length));

                read.Dangerous.ShouldBeFalse();
                read.Gunshot.ShouldBeTrue();
            }
        }
    }
}
