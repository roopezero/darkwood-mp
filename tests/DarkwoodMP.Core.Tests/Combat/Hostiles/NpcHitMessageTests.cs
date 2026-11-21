using System;
using System.Collections.Generic;
using DarkwoodMP.Combat.Damage;
using DarkwoodMP.Combat.Hostiles;
using DarkwoodMP.Net;
using DarkwoodMP.Tests.Net;
using DarkwoodMP.World;

namespace DarkwoodMP.Tests.Combat.Hostiles
{
    public class NpcHitMessageTests
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

        static readonly NpcHitRequest Stabbed = new NpcHitRequest
        {
            Npc = new ObjName(ObjKind.Saved, 2),
            Damage = 1f,
            Flags = NpcHitMessage.FlagsOf(true, false, true),
            Effects = new List<SentEffect> { Bleeding },
            Weapon = "{}",
        };

        static PacketWriter Written(NpcHitRequest hit)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(MsgType.NpcHit);
            NpcHitMessage.Write(w, hit);
            return w;
        }

        static byte[] BytesOf(PacketWriter w)
        {
            var bytes = new byte[w.Length];
            Array.Copy(w.Buffer, bytes, w.Length);
            return bytes;
        }

        static NpcHitRequest RoundTrip(NpcHitRequest hit)
        {
            var w = Written(hit);
            var r = new PacketReader(w.Buffer, w.Length);
            return NpcHitMessage.Read(r);
        }

        public class FlagsOf
        {
            [Fact]
            public void Sets_by_player_always()
            {
                NpcHitMessage.FlagsOf(false, false, false).ShouldBe((byte)2);
            }

            [Fact]
            public void Adds_cut_interrupt_and_normal()
            {
                NpcHitMessage.FlagsOf(true, true, true).ShouldBe((byte)15);
            }
        }

        public class Write
        {
            [Fact]
            public void Writes_the_npc_damage_flags_effects_and_weapon()
            {
                BytesOf(Written(Stabbed)).ShouldBe(new byte[]
                {
                    240,
                    1,
                    2, 0, 0, 0, 0, 0, 0, 0,
                    0x00, 0x00, 0x80, 0x3F,
                    11,
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
            public void Writes_no_effects_for_null_and_an_empty_weapon()
            {
                var hit = Stabbed with { Effects = null, Weapon = "" };

                BytesOf(Written(hit)).ShouldBe(new byte[]
                {
                    240,
                    1,
                    2, 0, 0, 0, 0, 0, 0, 0,
                    0x00, 0x00, 0x80, 0x3F,
                    11,
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
                var hit = RoundTrip(Stabbed);

                hit.Npc.ShouldBe(Stabbed.Npc);
                hit.Damage.ShouldBe(1f);
                hit.Flags.ShouldBe((byte)11);
                hit.Effects.Count.ShouldBe(1);
                hit.Effects[0].Sound.ShouldBe("a");
                hit.Weapon.ShouldBe("{}");
            }
        }
    }
}
