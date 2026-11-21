using System;
using DarkwoodMP.Net;
using DarkwoodMP.Sync.Players;
using DarkwoodMP.Tests.Net;

namespace DarkwoodMP.Tests.Sync.Players
{
    public class PlayerShotTests
    {
        static readonly MuzzleFlash Shot = new MuzzleFlash
        {
            Muzzle = "m",
            Particles = "",
            Flash = 1,
            Offset = new Vec3(1f, -2f, 0.5f),
        };

        static PacketWriter Written(ulong owner, MuzzleFlash shot)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.PlayerShot);
            PlayerShot.Write(w, owner, shot);
            return w;
        }

        public class Write
        {
            [Fact]
            public void Writes_the_owner_the_prefabs_the_flash_and_the_offset()
            {
                var w = Written(3, Shot);

                var bytes = new byte[w.Length];
                Array.Copy(w.Buffer, bytes, w.Length);
                bytes.ShouldBe(new byte[]
                {
                    51,
                    3, 0, 0, 0, 0, 0, 0, 0,
                    1, 0, 0x6D,
                    0, 0,
                    1,
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
                var w = Written(3, Shot with { Flash = 7 });
                var r = new PacketReader(w.Buffer, w.Length);

                var (owner, shot) = PlayerShot.Read(r);

                owner.ShouldBe(3UL);
                shot.Muzzle.ShouldBe("m");
                shot.Particles.ShouldBe("");
                shot.Flash.ShouldBe((byte)7);
                shot.Offset.Y.ShouldBe(-2f);
            }
        }
    }
}
