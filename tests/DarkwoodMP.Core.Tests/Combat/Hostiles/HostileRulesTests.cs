using DarkwoodMP.Combat.Hostiles;
using DarkwoodMP.World;

namespace DarkwoodMP.Tests.Combat.Hostiles
{
    public class HostileRulesTests
    {
        /// An NPC in play chasing a player.
        static readonly HostileFacts Chasing = new HostileFacts
        {
            IsNpc = true,
            InPlay = true,
            Chasing = true,
            TargetIsPlayer = true,
        };

        /// A friendly NPC in play, doing nothing.
        static readonly HostileFacts Calm = new HostileFacts
        {
            IsNpc = true,
            InPlay = true,
        };

        /// A client's NpcHit on an NPC the host has, in play, from a
        /// player with a stand-in.
        static readonly NpcAskFacts Ask = new NpcAskFacts
        {
            Found = true,
            IsNpc = true,
            InPlay = true,
            HasStandIn = true,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_hostile_limits()
            {
                var limits = new[]
                {
                    HostileRules.CalmTime,
                    HostileRules.CopyRange,
                    HostileRules.AttackReach,
                };

                limits.ShouldBe(new[] { 8f, 3000f, 3000f });
            }
        }

        public class TurnedHostile
        {
            [Fact]
            public void Says_an_npc_chasing_a_player_turned()
            {
                HostileRules.TurnedHostile(Chasing).ShouldBeTrue();
            }

            [Fact]
            public void Says_an_npc_that_attacks_players_on_sight_turned()
            {
                var npc = Calm with
                {
                    AttacksOnSight = true,
                    AttacksPlayers = true,
                };

                HostileRules.TurnedHostile(npc).ShouldBeTrue();
            }

            [Fact]
            public void Says_one_on_sight_that_spares_players_did_not()
            {
                var npc = Calm with { AttacksOnSight = true };

                HostileRules.TurnedHostile(npc).ShouldBeFalse();
            }

            [Fact]
            public void Says_one_chasing_something_else_did_not()
            {
                var npc = Chasing with { TargetIsPlayer = false };

                HostileRules.TurnedHostile(npc).ShouldBeFalse();
            }

            [Fact]
            public void Says_a_character_that_is_no_npc_did_not()
            {
                var npc = Chasing with { IsNpc = false };

                HostileRules.TurnedHostile(npc).ShouldBeFalse();
            }

            [Fact]
            public void Says_an_npc_out_of_play_did_not()
            {
                var npc = Chasing with { InPlay = false };

                HostileRules.TurnedHostile(npc).ShouldBeFalse();
            }
        }

        public class StillHostile
        {
            [Fact]
            public void Says_a_calm_npc_calmed_down()
            {
                HostileRules.StillHostile(Calm).ShouldBeFalse();
            }

            [Fact]
            public void Keeps_one_on_sight_hostile_even_sparing_players()
            {
                var npc = Calm with { AttacksOnSight = true };

                HostileRules.StillHostile(npc).ShouldBeTrue();
            }

            [Fact]
            public void Keeps_one_chasing_anything_hostile()
            {
                var npc = Calm with { Chasing = true };

                HostileRules.StillHostile(npc).ShouldBeTrue();
            }

            [Fact]
            public void Keeps_one_out_of_play_hostile_while_it_chases()
            {
                var npc = Chasing with { InPlay = false };

                HostileRules.StillHostile(npc).ShouldBeTrue();
            }
        }

        public class Running
        {
            [Fact]
            public void Runs_an_npc_in_the_running_set()
            {
                var npc = Calm with { Listed = true };

                HostileRules.Running(npc).ShouldBeTrue();
            }

            [Fact]
            public void Runs_an_npc_that_just_turned()
            {
                HostileRules.Running(Chasing).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_a_calm_npc_not_in_the_set()
            {
                HostileRules.Running(Calm).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_listed_character_that_is_no_npc()
            {
                var npc = Calm with { Listed = true, IsNpc = false };

                HostileRules.Running(npc).ShouldBeFalse();
            }
        }

        public class Resolve
        {
            [Fact]
            public void Allows_an_npc_the_host_has_in_play()
            {
                HostileRules.Resolve(Ask).ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_name_that_finds_nothing()
            {
                var ask = Ask with { Found = false };

                var verdict = HostileRules.Resolve(ask);

                verdict.Reason.ShouldBe(Refusal.NoSuchNpc);
            }

            [Fact]
            public void Refuses_a_character_that_is_no_npc()
            {
                var ask = Ask with { IsNpc = false };

                var verdict = HostileRules.Resolve(ask);

                verdict.Reason.ShouldBe(Refusal.NoSuchNpc);
            }

            [Fact]
            public void Refuses_an_npc_out_of_play_here()
            {
                var ask = Ask with { InPlay = false };

                var verdict = HostileRules.Resolve(ask);

                verdict.Reason.ShouldBe(Refusal.NoSuchNpc);
            }

            [Fact]
            public void Refuses_a_player_with_no_stand_in()
            {
                var ask = Ask with { HasStandIn = false };

                var verdict = HostileRules.Resolve(ask);

                verdict.Reason.ShouldBe(Refusal.NoStandIn);
            }

            [Fact]
            public void Refuses_a_missing_npc_before_a_missing_stand_in()
            {
                var ask = Ask with { InPlay = false, HasStandIn = false };

                var verdict = HostileRules.Resolve(ask);

                verdict.Reason.ShouldBe(Refusal.NoSuchNpc);
            }

            [Fact]
            public void Words_the_refusals_as_the_host_does()
            {
                var texts = new[]
                {
                    Refusals.Text(Verdict.Refuse(Refusal.NoSuchNpc)),
                    Refusals.Text(Verdict.Refuse(Refusal.NoStandIn)),
                };

                texts.ShouldBe(new[]
                {
                    "no such NPC here",
                    "no stand-in for that player yet",
                });
            }
        }

        public class MayAttack
        {
            /// The requester exactly AttackReach from the NPC.
            static readonly ReachFacts AtReach = new ReachFacts
            {
                Handling = true,
                Known = true,
                StandIn = new Vec2(0f, 0f),
                Target = new Vec2(3000f, 0f),
                Reach = HostileRules.AttackReach,
                What = "Wolfman",
            };

            [Fact]
            public void Allows_a_requester_exactly_at_the_reach()
            {
                var verdict = HostileRules.MayAttack(Ask, AtReach);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_requester_just_past_the_reach()
            {
                var reach = AtReach with { Target = new Vec2(3000.5f, 0f) };

                var verdict = HostileRules.MayAttack(Ask, reach);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Refuses_a_missing_npc_before_the_reach()
            {
                var ask = Ask with { Found = false };
                var reach = AtReach with { Target = new Vec2(9000f, 0f) };

                var verdict = HostileRules.MayAttack(ask, reach);

                verdict.Reason.ShouldBe(Refusal.NoSuchNpc);
            }
        }

        public class OurCopy
        {
            static readonly HostileCandidate Near = new HostileCandidate
            {
                Pos = new Vec3(100f, 0f, 0f),
                IsNpc = true,
                Kind = "wolfman",
            };

            static readonly Vec3 Origin = new Vec3(0f, 0f, 0f);

            static int Pick(params HostileCandidate[] ours) =>
                HostileRules.OurCopy(ours, "wolfman", Origin);

            [Fact]
            public void Picks_the_nearest_of_the_kind()
            {
                var far = Near with { Pos = new Vec3(200f, 0f, 0f) };

                Pick(far, Near).ShouldBe(1);
            }

            [Fact]
            public void Gives_a_tie_to_the_first()
            {
                var other = Near with { Pos = new Vec3(0f, 0f, 100f) };

                Pick(Near, other).ShouldBe(0);
            }

            [Fact]
            public void Leaves_one_exactly_at_the_range()
            {
                var edge = Near with { Pos = new Vec3(3000f, 0f, 0f) };

                Pick(edge).ShouldBe(-1);
            }

            [Fact]
            public void Picks_one_just_inside_the_range()
            {
                var edge = Near with { Pos = new Vec3(2999.5f, 0f, 0f) };

                Pick(edge).ShouldBe(0);
            }

            [Fact]
            public void Measures_the_range_in_three_dimensions()
            {
                var high = Near with { Pos = new Vec3(0f, 3000f, 0f) };

                Pick(high).ShouldBe(-1);
            }

            [Fact]
            public void Leaves_one_already_driven()
            {
                var driven = Near with { Driven = true };

                Pick(driven).ShouldBe(-1);
            }

            [Fact]
            public void Leaves_one_of_another_kind()
            {
                var other = Near with { Kind = "chicken lady" };

                Pick(other).ShouldBe(-1);
            }

            [Fact]
            public void Leaves_a_character_that_is_no_npc()
            {
                var beast = Near with { IsNpc = false };

                Pick(beast).ShouldBe(-1);
            }

            [Fact]
            public void Picks_the_corpse_of_an_npc_that_died_here()
            {
                var corpse = Near with { IsNpc = false, DiedHere = true };

                Pick(corpse).ShouldBe(0);
            }

            [Fact]
            public void Says_none_with_no_characters()
            {
                Pick().ShouldBe(-1);
            }
        }

        public class EffectsForwarded
        {
            static readonly ForwardFacts Just = new ForwardFacts
            {
                Forwarded = true,
                Frame = 120,
                ForwardedFrame = 120,
            };

            [Fact]
            public void Says_the_effects_of_a_hit_left_this_frame_went()
            {
                HostileRules.EffectsForwarded(Just).ShouldBeTrue();
            }

            [Fact]
            public void Says_effects_a_frame_later_are_ours()
            {
                var hit = Just with { Frame = 121 };

                HostileRules.EffectsForwarded(hit).ShouldBeFalse();
            }

            [Fact]
            public void Says_effects_on_another_npc_are_ours()
            {
                var hit = Just with { Forwarded = false };

                HostileRules.EffectsForwarded(hit).ShouldBeFalse();
            }

            [Fact]
            public void Says_effects_of_our_own_hit_are_ours()
            {
                var hit = Just with { Ours = true };

                HostileRules.EffectsForwarded(hit).ShouldBeFalse();
            }
        }
    }
}
