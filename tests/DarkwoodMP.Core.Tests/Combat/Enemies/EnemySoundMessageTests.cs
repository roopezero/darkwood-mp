using System;
using DarkwoodMP.Combat.Enemies;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;

namespace DarkwoodMP.Tests.Combat.Enemies
{
    public class EnemySoundMessageTests
    {
        static readonly EnemySound Bark = new EnemySound
        {
            Id = 4,
            Audio = "ab",
            Volume = 0.5f,
        };

        static PacketWriter Written(EnemySound s)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.EnemySound);
            EnemySoundMessage.Write(w, s);
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
            public void Writes_the_id_audio_and_volume()
            {
                BytesOf(Written(Bark)).ShouldBe(new byte[]
                {
                    18,
                    4, 0, 0, 0,
                    2, 0, (byte)'a', (byte)'b',
                    0x00, 0x00, 0x00, 0x3F,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_a_stopped_loop()
            {
                var w = Written(Bark with { Audio = "" });
                var s = EnemySoundMessage.Read(
                    new PacketReader(w.Buffer, w.Length));

                s.Id.ShouldBe(4u);
                s.Audio.ShouldBe("");
                s.Volume.ShouldBe(0.5f);
            }
        }
    }
}
