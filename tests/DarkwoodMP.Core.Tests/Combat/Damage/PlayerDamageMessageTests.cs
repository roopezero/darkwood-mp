using System;
using System.Collections.Generic;
using DarkwoodMP.Combat.Damage;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;

namespace DarkwoodMP.Tests.Combat.Damage
{
    public class PlayerDamageMessageTests
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

        static readonly StandInHit Bitten = new StandInHit
        {
            Damage = 1f,
            Shadow = true,
            Attacker = "dog",
            Effects = new List<SentEffect> { Bleeding },
        };

        static PacketWriter Written(StandInHit hit)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.PlayerDamage);
            PlayerDamageMessage.Write(w, hit);
            return w;
        }

        static byte[] BytesOf(PacketWriter w)
        {
            var bytes = new byte[w.Length];
            Array.Copy(w.Buffer, bytes, w.Length);
            return bytes;
        }

        static StandInHit RoundTrip(StandInHit hit)
        {
            var w = Written(hit);
            var r = new PacketReader(w.Buffer, w.Length);
            return PlayerDamageMessage.Read(r);
        }

        public class Write
        {
            [Fact]
            public void Writes_the_damage_flags_attacker_and_effects()
            {
                BytesOf(Written(Bitten)).ShouldBe(new byte[]
                {
                    13,
                    0x00, 0x00, 0x80, 0x3F,
                    1,
                    3, 0, (byte)'d', (byte)'o', (byte)'g',
                    1,
                    2, 0, 0, 0,
                    0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x80, 0x3F,
                    0x00, 0x00, 0x00, 0x40,
                    0x00, 0x00, 0x00, 0x3F,
                    5,
                    1, 0, (byte)'a',
                });
            }

            [Fact]
            public void Writes_no_effects_and_an_empty_sound_for_null()
            {
                var hit = Bitten with { Shadow = false, Effects = null };

                BytesOf(Written(hit)).ShouldBe(new byte[]
                {
                    13,
                    0x00, 0x00, 0x80, 0x3F,
                    0,
                    3, 0, (byte)'d', (byte)'o', (byte)'g',
                    0,
                });
            }

            [Fact]
            public void Writes_at_most_16_effects()
            {
                var many = new List<SentEffect>();
                for (int i = 0; i < 20; i++)
                {
                    many.Add(Bleeding);
                }

                var read = RoundTrip(Bitten with { Effects = many });

                read.Effects.Count.ShouldBe(16);
            }

            [Fact]
            public void Writes_a_null_sound_as_an_empty_string()
            {
                var quiet = Bleeding with { Sound = null };
                var hit = Bitten with
                {
                    Effects = new List<SentEffect> { quiet },
                };

                var bytes = BytesOf(Written(hit));

                bytes[^2].ShouldBe((byte)0);
                bytes[^1].ShouldBe((byte)0);
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var read = RoundTrip(Bitten);

                read.Damage.ShouldBe(1f);
                read.Shadow.ShouldBeTrue();
                read.Attacker.ShouldBe("dog");
                read.Effects.Count.ShouldBe(1);
                read.Effects[0].ShouldBe(Bleeding);
            }

            [Fact]
            public void Reads_an_empty_sound_as_null()
            {
                var quiet = Bleeding with { Sound = null };
                var hit = Bitten with
                {
                    Effects = new List<SentEffect> { quiet },
                };

                RoundTrip(hit).Effects[0].Sound.ShouldBeNull();
            }
        }
    }
}
