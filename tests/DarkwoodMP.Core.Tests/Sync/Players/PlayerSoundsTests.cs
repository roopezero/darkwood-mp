using System;
using DarkwoodMP.Net;
using DarkwoodMP.Sync.Players;
using DarkwoodMP.Tests.Net;

namespace DarkwoodMP.Tests.Sync.Players
{
    public class PlayerSoundsTests
    {
        static PacketWriter Written(ulong owner, params PlayedSound[] sounds)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.PlayerSounds);
            PlayerSounds.Write(w, owner, sounds);
            return w;
        }

        static byte[] Bytes(PacketWriter w)
        {
            var bytes = new byte[w.Length];
            Array.Copy(w.Buffer, bytes, w.Length);
            return bytes;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_owner_the_count_and_each_sound()
            {
                var w = Written(
                    2, new PlayedSound { Id = "ab", Volume = 1f });

                Bytes(w).ShouldBe(new byte[]
                {
                    50,
                    2, 0, 0, 0, 0, 0, 0, 0,
                    1,
                    2, 0, 0x61, 0x62,
                    0x00, 0x00, 0x80, 0x3F,
                });
            }

            [Fact]
            public void Writes_no_sounds_as_a_zero_count()
            {
                var w = Written(7);

                Bytes(w).ShouldBe(new byte[]
                {
                    50, 7, 0, 0, 0, 0, 0, 0, 0, 0,
                });
            }
        }

        public class ReadHead
        {
            [Fact]
            public void Reads_the_owner_and_the_count()
            {
                var w = Written(
                    9,
                    new PlayedSound { Id = "a", Volume = 1f },
                    new PlayedSound { Id = "b", Volume = 0.5f });
                var r = new PacketReader(w.Buffer, w.Length);

                var (owner, count) = PlayerSounds.ReadHead(r);

                owner.ShouldBe(9UL);
                count.ShouldBe(2);
            }
        }

        public class ReadSound
        {
            [Fact]
            public void Reads_each_sound_after_the_head()
            {
                var w = Written(
                    9,
                    new PlayedSound { Id = "step", Volume = 1f },
                    new PlayedSound { Id = "shot", Volume = 0.5f });
                var r = new PacketReader(w.Buffer, w.Length);
                PlayerSounds.ReadHead(r);

                var first = PlayerSounds.ReadSound(r);
                var second = PlayerSounds.ReadSound(r);

                first.Id.ShouldBe("step");
                first.Volume.ShouldBe(1f);
                second.Id.ShouldBe("shot");
                second.Volume.ShouldBe(0.5f);
            }
        }
    }
}
