using System;
using DarkwoodMP.Net;
using DarkwoodMP.Sync.Players;
using DarkwoodMP.Tests.Net;

namespace DarkwoodMP.Tests.Sync.Players
{
    public class PlayerHurtTests
    {
        static PacketWriter Written(ulong owner, HurtLook hurt)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.PlayerHurt);
            PlayerHurt.Write(w, owner, hurt);
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
            public void Writes_blood_with_its_prefab_height_and_yaw()
            {
                var w = Written(4, new HurtLook
                {
                    Kind = (byte)HurtKind.Blood,
                    Prefab = "x",
                    Height = 2f,
                    Yaw = -2f,
                });

                Bytes(w).ShouldBe(new byte[]
                {
                    52,
                    4, 0, 0, 0, 0, 0, 0, 0,
                    1,
                    1, 0, 0x78,
                    0x00, 0x00, 0x00, 0x40,
                    0x00, 0x00, 0x00, 0xC0,
                });
            }

            [Fact]
            public void Writes_burning_with_its_flames_and_sound()
            {
                var w = Written(4, new HurtLook
                {
                    Kind = (byte)HurtKind.Burning,
                    Prefab = "f",
                    Sound = "",
                });

                Bytes(w).ShouldBe(new byte[]
                {
                    52,
                    4, 0, 0, 0, 0, 0, 0, 0,
                    2,
                    1, 0, 0x66,
                    0, 0,
                });
            }

            [Fact]
            public void Writes_nothing_after_a_stop()
            {
                var w = Written(4, new HurtLook
                {
                    Kind = (byte)HurtKind.Stopped,
                    Prefab = "ignored",
                });

                Bytes(w).ShouldBe(new byte[]
                {
                    52, 4, 0, 0, 0, 0, 0, 0, 0, 3,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_blood()
            {
                var w = Written(5, new HurtLook
                {
                    Kind = (byte)HurtKind.Blood,
                    Prefab = "FX/Bloodsplats/a",
                    Height = 3f,
                    Yaw = 90f,
                });

                var r = new PacketReader(w.Buffer, w.Length);

                var (owner, hurt) = PlayerHurt.Read(r);

                owner.ShouldBe(5UL);
                hurt.Kind.ShouldBe((byte)HurtKind.Blood);
                hurt.Prefab.ShouldBe("FX/Bloodsplats/a");
                hurt.Sound.ShouldBe("");
                hurt.Height.ShouldBe(3f);
                hurt.Yaw.ShouldBe(90f);
            }

            [Fact]
            public void Reads_back_burning()
            {
                var w = Written(5, new HurtLook
                {
                    Kind = (byte)HurtKind.Burning,
                    Prefab = "f",
                    Sound = "s",
                });

                var r = new PacketReader(w.Buffer, w.Length);

                var (_, hurt) = PlayerHurt.Read(r);

                hurt.Prefab.ShouldBe("f");
                hurt.Sound.ShouldBe("s");
            }

            [Fact]
            public void Reads_only_the_kind_of_an_unknown_one()
            {
                var w = new PacketWriter(new RecordingLog());
                w.Begin(MsgType.PlayerHurt);
                w.W.Write(6UL);
                w.W.Write((byte)9);
                var r = new PacketReader(w.Buffer, w.Length);

                var (owner, hurt) = PlayerHurt.Read(r);

                owner.ShouldBe(6UL);
                hurt.Kind.ShouldBe((byte)9);
                hurt.Prefab.ShouldBe("");
            }
        }
    }
}
