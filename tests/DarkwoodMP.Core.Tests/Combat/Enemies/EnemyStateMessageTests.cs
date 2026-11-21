using System;
using DarkwoodMP.Combat.Enemies;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;

namespace DarkwoodMP.Tests.Combat.Enemies
{
    public class EnemyStateMessageTests
    {
        static readonly EnemyAnim Walking = new EnemyAnim
        {
            Animator = 2,
            Clip = 3,
            Frame = 258,
            Playing = true,
        };

        static readonly EnemyFrame Dog = new EnemyFrame
        {
            Id = 9,
            At = new Vec3(1f, 0f, 2f),
            Rot = new Quat(0f, 0f, 0f, 1f),
            Flags = EnemyStateMessage.FlagsOf(true, false, true),
            Health = 0.5f,
        };

        static PacketWriter Written(int enemies)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.EnemyState);
            int at = EnemyStateMessage.WriteHead(w, 2f);
            for (int i = 0; i < enemies; i++)
            {
                EnemyStateMessage.WriteFrame(w, Dog, 1);
                EnemyStateMessage.WriteAnim(w, Walking);
            }

            EnemyStateMessage.SetCount(w, at, enemies);
            return w;
        }

        static byte[] BytesOf(PacketWriter w)
        {
            var bytes = new byte[w.Length];
            Array.Copy(w.Buffer, bytes, w.Length);
            return bytes;
        }

        public class FlagsOf
        {
            [Fact]
            public void Sets_alive_asleep_and_cut()
            {
                EnemyStateMessage.FlagsOf(true, true, true)
                    .ShouldBe((byte)7);
            }

            [Fact]
            public void Sets_none_for_a_dead_one_awake()
            {
                EnemyStateMessage.FlagsOf(false, false, false)
                    .ShouldBe((byte)0);
            }
        }

        public class Write
        {
            [Fact]
            public void Writes_the_clock_count_enemy_and_animator()
            {
                BytesOf(Written(1)).ShouldBe(new byte[]
                {
                    15,
                    0x00, 0x00, 0x00, 0x40,
                    1, 0,
                    9, 0, 0, 0,
                    0x00, 0x00, 0x80, 0x3F,
                    0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x40,
                    0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x80, 0x3F,
                    5,
                    0x00, 0x00, 0x00, 0x3F,
                    1,
                    2, 0, 0, 0,
                    3, 0, 0, 0,
                    2, 1,
                    1,
                });
            }

            [Fact]
            public void Writes_a_count_over_255_in_two_bytes()
            {
                var bytes = BytesOf(Written(258));

                bytes[5].ShouldBe((byte)2);
                bytes[6].ShouldBe((byte)1);
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(2);
                var states = EnemyStateMessage.Read(
                    new PacketReader(w.Buffer, w.Length));

                states.Sent.ShouldBe(2f);
                states.Frames.Count.ShouldBe(2);
                var e = states.Frames[1];
                e.Id.ShouldBe(9u);
                e.At.ShouldBe(Dog.At);
                e.Flags.ShouldBe((byte)5);
                e.Health.ShouldBe(0.5f);
                e.Anims.Length.ShouldBe(1);
                e.Anims[0].Frame.ShouldBe((ushort)258);
                e.Anims[0].Playing.ShouldBeTrue();
            }

            [Fact]
            public void Reads_an_empty_state()
            {
                var w = Written(0);
                var states = EnemyStateMessage.Read(
                    new PacketReader(w.Buffer, w.Length));

                states.Frames.ShouldBeEmpty();
            }
        }
    }
}
