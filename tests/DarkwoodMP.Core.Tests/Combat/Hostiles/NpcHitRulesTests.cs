using DarkwoodMP.Combat.Hostiles;

namespace DarkwoodMP.Tests.Combat.Hostiles
{
    public class NpcHitRulesTests
    {
        /// Our player's 25-damage melee hit on a living NPC the host has.
        static readonly NpcHitFacts Melee = new NpcHitFacts
        {
            IsNpc = true,
            Alive = true,
            ByPlayer = true,
            Damage = 25f,
        };

        /// A scripted attack by an NPC of ours, in the host's world.
        static readonly NpcAttackFacts Attack = new NpcAttackFacts
        {
            IsNpc = true,
            ClientMode = true,
        };

        const string NotHere = "no such NPC here";

        /// The host refused our hit on an NPC it hasn't got.
        static readonly NpcAnswerFacts Refused = new NpcAnswerFacts
        {
            Reason = NotHere,
            Alive = true,
            HasPlayer = true,
        };

        public class Way
        {
            [Fact]
            public void Sends_our_player_s_hit_to_the_host()
            {
                NpcHitRules.Way(Melee).ShouldBe(NpcHitWay.Sent);
            }

            [Fact]
            public void Leaves_our_own_hit_to_the_game()
            {
                var hit = Melee with { Ours = true };

                NpcHitRules.Way(hit).ShouldBe(NpcHitWay.Game);
            }

            [Fact]
            public void Leaves_a_hit_on_a_character_that_is_no_npc()
            {
                var hit = Melee with { IsNpc = false, Alive = false };

                NpcHitRules.Way(hit).ShouldBe(NpcHitWay.Game);
            }

            [Theory]
            [InlineData(false, false, false)]
            [InlineData(true, true, false)]
            [InlineData(true, false, true)]
            public void Keeps_the_hit_ours_for_one_the_host_has_not(
                bool alive,
                bool trader,
                bool local)
            {
                var hit = Melee with
                {
                    Alive = alive,
                    NightTrader = trader,
                    LocalOnly = local,
                };

                NpcHitRules.Way(hit).ShouldBe(NpcHitWay.Ours);
            }

            [Fact]
            public void Keeps_a_dead_npc_s_hit_ours_before_a_throw()
            {
                var hit = Melee with { Alive = false, Thrown = true };

                NpcHitRules.Way(hit).ShouldBe(NpcHitWay.Ours);
            }

            [Fact]
            public void Leaves_a_thrown_hit_to_the_host_s_throw()
            {
                var hit = Melee with { Thrown = true, ByPlayer = false };

                NpcHitRules.Way(hit).ShouldBe(NpcHitWay.Thrown);
            }

            [Fact]
            public void Leaves_a_thrown_hit_to_the_host_before_a_flame()
            {
                var hit = Melee with { Thrown = true, Flame = true };

                NpcHitRules.Way(hit).ShouldBe(NpcHitWay.Thrown);
            }

            [Fact]
            public void Leaves_another_s_flame_to_the_game()
            {
                var hit = Melee with { Flame = true };

                NpcHitRules.Way(hit).ShouldBe(NpcHitWay.Game);
            }

            [Fact]
            public void Sends_our_own_flame_s_hit()
            {
                var hit = Melee with { Flame = true, FlameOurs = true };

                NpcHitRules.Way(hit).ShouldBe(NpcHitWay.Sent);
            }

            [Fact]
            public void Leaves_what_the_world_does_to_the_game()
            {
                var hit = Melee with { ByPlayer = false };

                NpcHitRules.Way(hit).ShouldBe(NpcHitWay.Game);
            }

            [Fact]
            public void Sends_a_hit_from_an_attacker_with_a_player()
            {
                var hit = Melee with
                {
                    ByPlayer = false,
                    AttackerIsPlayer = true,
                };

                NpcHitRules.Way(hit).ShouldBe(NpcHitWay.Sent);
            }

            [Fact]
            public void Leaves_a_hit_of_no_damage_and_no_effects()
            {
                var hit = Melee with { Damage = 0f };

                NpcHitRules.Way(hit).ShouldBe(NpcHitWay.Game);
            }

            [Fact]
            public void Sends_a_hit_of_no_damage_with_an_effect()
            {
                var hit = Melee with { Damage = 0f, Effects = 1 };

                NpcHitRules.Way(hit).ShouldBe(NpcHitWay.Sent);
            }

            [Fact]
            public void Sends_a_hit_of_just_over_no_damage()
            {
                var hit = Melee with { Damage = 0.01f };

                NpcHitRules.Way(hit).ShouldBe(NpcHitWay.Sent);
            }

            [Fact]
            public void Sends_a_hit_whose_damage_is_not_a_number()
            {
                var hit = Melee with { Damage = float.NaN };

                NpcHitRules.Way(hit).ShouldBe(NpcHitWay.Sent);
            }
        }

        public class AfterHit
        {
            [Fact]
            public void Applies_the_host_s_result_when_accepted()
            {
                var answer = Refused with { Accepted = true };

                var after = NpcHitRules.AfterHit(answer);

                after.ShouldBe(NpcHitAnswer.HostApplied);
            }

            [Fact]
            public void Hits_here_an_npc_the_host_has_not_got()
            {
                var after = NpcHitRules.AfterHit(Refused);

                after.ShouldBe(NpcHitAnswer.OursHere);
            }

            [Fact]
            public void Does_nothing_for_another_refusal()
            {
                var answer = Refused with
                {
                    Reason = "no stand-in for that player yet",
                };

                var after = NpcHitRules.AfterHit(answer);

                after.ShouldBe(NpcHitAnswer.Nothing);
            }

            [Fact]
            public void Does_nothing_for_an_npc_dead_by_then()
            {
                var answer = Refused with { Alive = false };

                var after = NpcHitRules.AfterHit(answer);

                after.ShouldBe(NpcHitAnswer.Nothing);
            }

            [Fact]
            public void Does_nothing_with_no_player_of_ours()
            {
                var answer = Refused with { HasPlayer = false };

                var after = NpcHitRules.AfterHit(answer);

                after.ShouldBe(NpcHitAnswer.Nothing);
            }
        }

        public class AttackWay
        {
            [Fact]
            public void Sends_a_scripted_attack_to_the_host()
            {
                NpcHitRules.AttackWay(Attack).ShouldBe(NpcAttackWay.Send);
            }

            [Fact]
            public void Leaves_our_own_attack_to_the_game()
            {
                var attack = Attack with { Ours = true, Copy = true };

                var way = NpcHitRules.AttackWay(attack);

                way.ShouldBe(NpcAttackWay.Game);
            }

            [Theory]
            [InlineData(false, false, true, false)]
            [InlineData(true, true, true, false)]
            [InlineData(true, false, false, false)]
            [InlineData(true, false, true, true)]
            public void Leaves_the_attack_to_the_game(
                bool npc,
                bool dummy,
                bool client,
                bool local)
            {
                var attack = Attack with
                {
                    IsNpc = npc,
                    Dummy = dummy,
                    ClientMode = client,
                    LocalOnly = local,
                };

                var way = NpcHitRules.AttackWay(attack);

                way.ShouldBe(NpcAttackWay.Game);
            }

            [Fact]
            public void Leaves_a_local_npc_to_the_game_before_a_copy()
            {
                var attack = Attack with { LocalOnly = true, Copy = true };

                var way = NpcHitRules.AttackWay(attack);

                way.ShouldBe(NpcAttackWay.Game);
            }

            [Theory]
            [InlineData(true, false, false, false)]
            [InlineData(false, true, false, false)]
            [InlineData(false, false, true, false)]
            [InlineData(false, false, false, true)]
            public void Skips_an_attack_the_host_has_made(
                bool copy,
                bool frozen,
                bool replaying,
                bool shared)
            {
                var attack = Attack with
                {
                    Copy = copy,
                    Frozen = frozen,
                    Replaying = replaying,
                    SharedOnly = shared,
                };

                var way = NpcHitRules.AttackWay(attack);

                way.ShouldBe(NpcAttackWay.Skip);
            }
        }

        public class OursToAttack
        {
            [Fact]
            public void Attacks_here_for_an_npc_the_host_has_not_got()
            {
                NpcHitRules.OursToAttack(false, NotHere, true).ShouldBeTrue();
            }

            [Theory]
            [InlineData(true, NotHere, true)]
            [InlineData(false, "not here", true)]
            [InlineData(false, NotHere, false)]
            public void Leaves_the_attack_otherwise(
                bool accepted,
                string reason,
                bool alive)
            {
                var ours = NpcHitRules.OursToAttack(accepted, reason, alive);

                ours.ShouldBeFalse();
            }
        }
    }
}
