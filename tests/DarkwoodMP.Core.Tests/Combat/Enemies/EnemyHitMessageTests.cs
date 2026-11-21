using System;
using System.Collections.Generic;
using DarkwoodMP.Combat.Damage;
using DarkwoodMP.Combat.Enemies;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;

namespace DarkwoodMP.Tests.Combat.Enemies
{
    public class EnemyHitMessageTests
    {
        static readonly SentEffect Bleeding = new SentEffect
        {
            Type = 2,
            StartDelay = 0f,
            Duration = 1f,
            Modifier = 2f,
            Interval = 0.5f,
            StopsBleeding = true,
            PoisonOverlay = true,
            Sound = "a",
        };

        static readonly EnemyHitRequest Stabbed = new EnemyHitRequest
        {
            Id = 6,
            Damage = 1f,
            Flags = EnemyHitMessage.Cut | EnemyHitMessage.ByPlayer,
            Effects = new List<SentEffect> { Bleeding },
            Weapon = "{}",
        };

        static PacketWriter Written(EnemyHitRequest hit)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.EnemyHit);
            EnemyHitMessage.Write(w, hit);
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
            public void Writes_the_id_damage_flags_effects_and_weapon()
            {
                BytesOf(Written(Stabbed)).ShouldBe(new byte[]
                {
                    17,
                    6, 0, 0, 0,
                    0x00, 0x00, 0x80, 0x3F,
                    3,
                    1,
                    2, 0, 0, 0,
                    0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x80, 0x3F,
                    0x00, 0x00, 0x00, 0x40,
                    0x00, 0x00, 0x00, 0x3F,
                    5,
                    1, 0, (byte)'a',
                    2, 0, (byte)'{', (byte)'}',
                });
            }

            [Fact]
            public void Writes_no_effects_for_null_and_bare_hands()
            {
                var hit = Stabbed with { Effects = null, Weapon = "" };

                BytesOf(Written(hit)).ShouldBe(new byte[]
                {
                    17,
                    6, 0, 0, 0,
                    0x00, 0x00, 0x80, 0x3F,
                    3,
                    0,
                    0, 0,
                });
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = Written(Stabbed);
                var hit = EnemyHitMessage.Read(
                    new PacketReader(w.Buffer, w.Length));

                hit.Id.ShouldBe(6u);
                hit.Damage.ShouldBe(1f);
                hit.Flags.ShouldBe((byte)3);
                hit.Effects.Count.ShouldBe(1);
                hit.Effects[0].Sound.ShouldBe("a");
                hit.Weapon.ShouldBe("{}");
            }
        }
    }
}
