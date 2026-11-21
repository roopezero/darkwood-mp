using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Combat.Targeting;

namespace DarkwoodMP.Tests.Combat.Targeting
{
    public partial class ThreatRulesTests
    {
        /// A living enemy the host runs, with threat on, two characters in
        /// view and no target.
        static readonly EnemyFacts Hunter = new EnemyFacts
        {
            On = true,
            Exists = true,
            Alive = true,
            Eligible = true,
            Seen = 2,
        };

        /// A player beside the enemy with no stored threat: 25 from
        /// closeness alone.
        static readonly SightCandidate Near = new SightCandidate
        {
            Slot = 0,
            Stored = 0f,
            Distance = 0f,
        };

        /// A player at the edge of a 1000 range: their score is exactly
        /// their stored threat.
        static readonly SightCandidate Edge = new SightCandidate
        {
            Slot = 1,
            Stored = 10f,
            Distance = 1000f,
        };

        /// The host's living player, at the edge of a 1500 range.
        static readonly PlayerCandidate Host = new PlayerCandidate
        {
            Host = true,
            Alive = true,
            Stored = 5f,
            Distance = 1500f,
        };

        /// A living, visible stand-in at the edge of a 1500 range.
        static readonly PlayerCandidate Joiner = new PlayerCandidate
        {
            Alive = true,
            Stored = 5f,
            Distance = 1500f,
        };

        static int[] Slots(List<SightCandidate> players) =>
            players.Select(p => p.Slot).ToArray();

        static float[] Parts(Vec3 v) => new[] { v.X, v.Y, v.Z };

        public class Limits
        {
            [Fact]
            public void Pins_the_threat_limits()
            {
                var limits = new[]
                {
                    ThreatRules.NoiseWeight,
                    ThreatRules.MinVolume,
                    ThreatRules.SeenPerSecond,
                    ThreatRules.MaxSeenSpan,
                    ThreatRules.CloseWeight,
                    ThreatRules.MinSightRange,
                    ThreatRules.MinCloseRange,
                    ThreatRules.CloseRangeSights,
                    ThreatRules.HalfLife,
                    ThreatRules.MinStored,
                    ThreatRules.SwitchMargin,
                };

                limits.ShouldBe(new[]
                {
                    6f, 0.1f, 2f, 2f, 25f, 1f, 1500f, 3f, 15f, 0.05f, 1.3f,
                });
            }

            [Fact]
            public void Pins_the_view_entries_the_sight_check_needs()
            {
                ThreatRules.MinSeen.ShouldBe(2);
            }
        }

        public class IsOn
        {
            [Fact]
            public void Turns_on_with_one_stand_in()
            {
                ThreatRules.IsOn(1).ShouldBeTrue();
            }

            [Fact]
            public void Stays_off_with_no_stand_in()
            {
                ThreatRules.IsOn(0).ShouldBeFalse();
            }
        }

        public class CountsHit
        {
            [Theory]
            [InlineData(true, true, true)]
            [InlineData(false, true, false)]
            [InlineData(true, false, false)]
            public void Counts_only_a_player_s_hit_while_on(
                bool on,
                bool byPlayer,
                bool counts)
            {
                var result = ThreatRules.CountsHit(on, byPlayer);

                result.ShouldBe(counts);
            }
        }

        public class NoiseGain
        {
            [Theory]
            [InlineData(2f, 12f)]
            [InlineData(0.1f, 0.6f)]
            [InlineData(0.09f, 0.6f)]
            [InlineData(0f, 0.6f)]
            [InlineData(-3f, 0.6f)]
            [InlineData(30f, 180f)]
            public void Weighs_the_volume_with_a_floor(
                float volume,
                float gain)
            {
                var result = ThreatRules.NoiseGain(volume);

                result.ShouldBe(gain, 0.0001f);
            }
        }

        public class SeenSpan
        {
            [Theory]
            [InlineData(11.5f, 1.5f)]
            [InlineData(12f, 2f)]
            [InlineData(12.01f, 2f)]
            [InlineData(10f, 0f)]
            [InlineData(9f, -1f)]
            public void Measures_from_the_last_gain_up_to_two_seconds(
                float now,
                float span)
            {
                var result = ThreatRules.SeenSpan(
                    new Seconds(now),
                    new Seconds(10f));

                result.ShouldBe(span, 0.0001f);
            }
        }

        public class SeenGain
        {
            [Fact]
            public void Gives_two_a_second()
            {
                ThreatRules.SeenGain(1.5f).ShouldBe(3f);
            }
        }

        public class CountsSeen
        {
            [Fact]
            public void Counts_a_living_enemy_while_on()
            {
                ThreatRules.CountsSeen(Hunter).ShouldBeTrue();
            }

            [Fact]
            public void Counts_a_friendly_npc_too()
            {
                var npc = Hunter with { Npc = true, Eligible = false };

                ThreatRules.CountsSeen(npc).ShouldBeTrue();
            }

            [Fact]
            public void Skips_when_off()
            {
                var enemy = Hunter with { On = false };

                ThreatRules.CountsSeen(enemy).ShouldBeFalse();
            }

            [Fact]
            public void Skips_no_enemy()
            {
                var enemy = Hunter with { Exists = false };

                ThreatRules.CountsSeen(enemy).ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_dead_enemy()
            {
                var enemy = Hunter with { Alive = false };

                ThreatRules.CountsSeen(enemy).ShouldBeFalse();
            }
        }

        public class GainsSeen
        {
            [Theory]
            [InlineData(true, true, false, true)]
            [InlineData(false, true, false, false)]
            [InlineData(true, false, false, false)]
            [InlineData(true, true, true, false)]
            public void Gives_threat_to_a_living_visible_player(
                bool player,
                bool alive,
                bool invisible,
                bool gains)
            {
                var result = ThreatRules.GainsSeen(player, alive, invisible);

                result.ShouldBe(gains);
            }
        }

        public class BeforeSight
        {
            [Fact]
            public void Reorders_for_a_living_enemy_with_two_in_view()
            {
                ThreatRules.BeforeSight(Hunter).ShouldBe(SightStep.Reorder);
            }

            [Fact]
            public void Leaves_the_list_when_off()
            {
                var enemy = Hunter with { On = false };

                ThreatRules.BeforeSight(enemy).ShouldBe(SightStep.Leave);
            }

            [Fact]
            public void Leaves_the_list_of_no_enemy()
            {
                var enemy = Hunter with { Exists = false };

                ThreatRules.BeforeSight(enemy).ShouldBe(SightStep.Leave);
            }

            [Fact]
            public void Leaves_the_list_of_a_dead_enemy()
            {
                var enemy = Hunter with { Alive = false };

                ThreatRules.BeforeSight(enemy).ShouldBe(SightStep.Leave);
            }

            [Fact]
            public void Hands_a_friendly_npc_to_the_npc_step()
            {
                var npc = Hunter with { Npc = true, Eligible = false };

                ThreatRules.BeforeSight(npc).ShouldBe(SightStep.FriendlyNpc);
            }

            [Fact]
            public void Hands_a_friendly_npc_over_before_the_count_check()
            {
                var npc = Hunter with
                {
                    Npc = true,
                    Eligible = false,
                    Seen = 1,
                };

                ThreatRules.BeforeSight(npc).ShouldBe(SightStep.FriendlyNpc);
            }

            [Fact]
            public void Leaves_a_dead_friendly_npc_alone()
            {
                var npc = Hunter with
                {
                    Npc = true,
                    Eligible = false,
                    Alive = false,
                };

                ThreatRules.BeforeSight(npc).ShouldBe(SightStep.Leave);
            }

            [Fact]
            public void Reorders_for_an_npc_the_host_runs_as_an_enemy()
            {
                var npc = Hunter with { Npc = true };

                ThreatRules.BeforeSight(npc).ShouldBe(SightStep.Reorder);
            }

            [Fact]
            public void Leaves_a_list_of_one()
            {
                var enemy = Hunter with { Seen = 1 };

                ThreatRules.BeforeSight(enemy).ShouldBe(SightStep.Leave);
            }

            [Fact]
            public void Leaves_an_enemy_after_something_else()
            {
                var enemy = Hunter with { HasTarget = true };

                ThreatRules.BeforeSight(enemy).ShouldBe(SightStep.Leave);
            }

            [Fact]
            public void Reorders_for_an_enemy_after_a_player()
            {
                var enemy = Hunter with
                {
                    HasTarget = true,
                    TargetIsPlayer = true,
                };

                ThreatRules.BeforeSight(enemy).ShouldBe(SightStep.Reorder);
            }
        }

        public class InSightOrder
        {
            [Theory]
            [InlineData(true, true, true)]
            [InlineData(true, false, false)]
            [InlineData(false, true, false)]
            public void Orders_only_living_players(
                bool player,
                bool alive,
                bool ordered)
            {
                var result = ThreatRules.InSightOrder(player, alive);

                result.ShouldBe(ordered);
            }
        }

        public class SightRange
        {
            [Fact]
            public void Multiplies_the_far_sight_by_its_modifier()
            {
                ThreatRules.SightRange(800f, 1.5f).ShouldBe(1200f);
            }

            [Fact]
            public void Is_at_least_one()
            {
                ThreatRules.SightRange(0f, 1f).ShouldBe(1f);
            }
        }

        public class CloseRange
        {
            [Fact]
            public void Is_three_times_the_far_sight()
            {
                ThreatRules.CloseRange(1000f, 1f).ShouldBe(3000f);
            }

            [Fact]
            public void Is_exactly_the_least_range_at_it()
            {
                ThreatRules.CloseRange(500f, 1f).ShouldBe(1500f);
            }

            [Fact]
            public void Is_at_least_the_least_range()
            {
                ThreatRules.CloseRange(0f, 1f).ShouldBe(1500f);
            }
        }

        public class Score
        {
            [Theory]
            [InlineData(3f, 0f, 28f)]
            [InlineData(3f, 500f, 15.5f)]
            [InlineData(3f, 1000f, 3f)]
            [InlineData(3f, 1001f, 3f)]
            [InlineData(0f, 4000f, 0f)]
            public void Adds_closeness_to_the_stored_threat(
                float stored,
                float distance,
                float score)
            {
                var result = ThreatRules.Score(stored, distance, 1000f);

                result.ShouldBe(score, 0.0001f);
            }
        }

        public class Order
        {
            [Fact]
            public void Changes_nothing_with_one_player()
            {
                var players = new List<SightCandidate> { Edge };

                var changed = ThreatRules.Order(players, 1000f);

                changed.ShouldBeFalse();
            }

            [Fact]
            public void Puts_the_most_threat_last()
            {
                var players = new List<SightCandidate>
                {
                    Edge with { Slot = 0, Stored = 20f },
                    Edge with { Slot = 1, Stored = 5f },
                    Edge with { Slot = 2, Stored = 12f },
                };

                ThreatRules.Order(players, 1000f);

                Slots(players).ShouldBe(new[] { 1, 2, 0 });
            }

            [Fact]
            public void Picks_a_far_player_with_more_threat_over_the_closest()
            {
                var far = Edge with { Slot = 1, Stored = 30f, Distance = 900f };
                var players = new List<SightCandidate> { Near, far };

                ThreatRules.Order(players, 1000f);

                players[1].Slot.ShouldBe(1);
            }

            [Fact]
            public void Picks_the_closest_only_through_closeness()
            {
                var far = Edge with { Slot = 1, Stored = 24f };
                var players = new List<SightCandidate> { far, Near };

                ThreatRules.Order(players, 1000f);

                players[1].Slot.ShouldBe(0);
            }

            [Fact]
            public void Sets_each_score()
            {
                var players = new List<SightCandidate> { Near, Edge };

                ThreatRules.Order(players, 1000f);

                players.Select(p => p.Score).ShouldBe(new[] { 10f, 25f });
            }

            [Fact]
            public void Keeps_the_current_target_just_below_the_margin()
            {
                var players = new List<SightCandidate>
                {
                    Edge with { Slot = 0, Stored = 100f, Current = true },
                    Edge with { Slot = 1, Stored = 129.9f },
                };

                ThreatRules.Order(players, 1000f);

                players[1].Slot.ShouldBe(0);
            }

            [Fact]
            public void Switches_at_exactly_the_margin()
            {
                var players = new List<SightCandidate>
                {
                    Edge with { Slot = 0, Stored = 100f, Current = true },
                    Edge with { Slot = 1, Stored = 130f },
                };

                ThreatRules.Order(players, 1000f);

                players[1].Slot.ShouldBe(1);
            }

            [Fact]
            public void Moves_the_kept_target_to_the_end_only()
            {
                var players = new List<SightCandidate>
                {
                    Edge with { Slot = 0, Stored = 110f },
                    Edge with { Slot = 1, Stored = 100f, Current = true },
                    Edge with { Slot = 2, Stored = 120f },
                };

                ThreatRules.Order(players, 1000f);

                Slots(players).ShouldBe(new[] { 0, 2, 1 });
            }

            [Fact]
            public void Never_keeps_a_current_target_scoring_nothing()
            {
                var players = new List<SightCandidate>
                {
                    Edge with { Slot = 0, Stored = 0f, Current = true },
                    Edge with { Slot = 1, Stored = 0.1f },
                };

                ThreatRules.Order(players, 1000f);

                players[1].Slot.ShouldBe(1);
            }

            [Fact]
            public void Leaves_a_current_target_that_is_already_last()
            {
                var players = new List<SightCandidate>
                {
                    Edge with { Slot = 0, Stored = 50f, Current = true },
                    Edge with { Slot = 1, Stored = 1f },
                };

                ThreatRules.Order(players, 1000f);

                Slots(players).ShouldBe(new[] { 1, 0 });
            }
        }
    }
}
