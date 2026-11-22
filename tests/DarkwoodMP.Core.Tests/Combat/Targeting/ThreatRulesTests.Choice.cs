using System.Collections.Generic;
using DarkwoodMP.Combat.Targeting;

namespace DarkwoodMP.Tests.Combat.Targeting
{
    /// The rules that choose "the player" for an enemy, and where it runs
    /// from.
    public partial class ThreatRulesTests
    {
        public class ChoosesByThreat
        {
            [Fact]
            public void Chooses_for_an_enemy_the_host_runs()
            {
                ThreatRules.ChoosesByThreat(Hunter).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_a_friendly_npc_to_the_host_s_player()
            {
                var npc = Hunter with { Npc = true, Eligible = false };

                ThreatRules.ChoosesByThreat(npc).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_it_to_the_host_s_player_when_off()
            {
                var enemy = Hunter with { On = false };

                ThreatRules.ChoosesByThreat(enemy).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_it_to_the_host_s_player_with_no_enemy()
            {
                var enemy = Hunter with { Exists = false };

                ThreatRules.ChoosesByThreat(enemy).ShouldBeFalse();
            }

            [Fact]
            public void Chooses_for_a_dead_enemy_too()
            {
                var enemy = Hunter with { Alive = false };

                ThreatRules.ChoosesByThreat(enemy).ShouldBeTrue();
            }
        }

        public class Best
        {
            [Fact]
            public void Gives_the_host_with_no_candidates()
            {
                var players = new List<PlayerCandidate>();

                ThreatRules.Best(players, 1500f).ShouldBe(-1);
            }

            [Fact]
            public void Picks_a_far_player_with_more_threat_over_the_closest()
            {
                var near = Host with { Stored = 0f, Distance = 0f };
                var far = Joiner with { Stored = 30f, Distance = 1400f };
                var players = new List<PlayerCandidate> { near, far };

                ThreatRules.Best(players, 1500f).ShouldBe(1);
            }

            [Fact]
            public void Picks_the_higher_score_by_any_amount()
            {
                var more = Joiner with { Stored = 5.01f };
                var players = new List<PlayerCandidate> { Host, more };

                ThreatRules.Best(players, 1500f).ShouldBe(1);
            }

            [Fact]
            public void Picks_the_nearer_on_an_equal_score()
            {
                var nearer = Joiner with { Distance = 1600f };
                var farther = Joiner with { Distance = 2000f };
                var players = new List<PlayerCandidate> { farther, nearer };

                ThreatRules.Best(players, 1500f).ShouldBe(1);
            }

            [Fact]
            public void Keeps_the_earlier_on_an_equal_score_and_distance()
            {
                var players = new List<PlayerCandidate> { Host, Joiner };

                ThreatRules.Best(players, 1500f).ShouldBe(0);
            }

            [Fact]
            public void Counts_an_invisible_host()
            {
                var host = Host with { Invisible = true, Stored = 9f };
                var players = new List<PlayerCandidate> { host, Joiner };

                ThreatRules.Best(players, 1500f).ShouldBe(0);
            }

            [Fact]
            public void Skips_a_dying_host()
            {
                var host = Host with { Dying = true, Stored = 9f };
                var players = new List<PlayerCandidate> { host, Joiner };

                ThreatRules.Best(players, 1500f).ShouldBe(1);
            }

            [Fact]
            public void Skips_a_dead_host()
            {
                var host = Host with { Alive = false, Stored = 9f };
                var players = new List<PlayerCandidate> { host, Joiner };

                ThreatRules.Best(players, 1500f).ShouldBe(1);
            }

            [Fact]
            public void Skips_an_invisible_stand_in()
            {
                var hidden = Joiner with { Invisible = true, Stored = 9f };
                var players = new List<PlayerCandidate> { Host, hidden };

                ThreatRules.Best(players, 1500f).ShouldBe(0);
            }

            [Fact]
            public void Counts_a_dying_stand_in()
            {
                var dying = Joiner with { Dying = true, Stored = 9f };
                var players = new List<PlayerCandidate> { Host, dying };

                ThreatRules.Best(players, 1500f).ShouldBe(1);
            }

            [Fact]
            public void Skips_a_dead_stand_in()
            {
                var dead = Joiner with { Alive = false, Stored = 9f };
                var players = new List<PlayerCandidate> { Host, dead };

                ThreatRules.Best(players, 1500f).ShouldBe(0);
            }

            [Fact]
            public void Gives_the_host_when_no_candidate_counts()
            {
                var host = Host with { Alive = false };
                var hidden = Joiner with { Invisible = true };
                var players = new List<PlayerCandidate> { host, hidden };

                ThreatRules.Best(players, 1500f).ShouldBe(-1);
            }
        }

        public class Nearest
        {
            /// Threat on, the host's player 500 from the enemy.
            static readonly NearestFacts Near = new NearestFacts
            {
                On = true,
                HasHost = true,
                HostAt = new Vec3(500f, 0f, 0f),
                HostDistance = 500f,
                Exists = true,
                EnemyAt = new Vec3(0f, 0f, 0f),
            };

            static readonly StandInSpot Joiner = new StandInSpot
            {
                Alive = true,
                At = new Vec3(0f, 0f, 300f),
                Distance = 300f,
            };

            [Fact]
            public void Gives_a_nearer_living_stand_in()
            {
                var standIns = new List<StandInSpot> { Joiner };

                var at = ThreatRules.Nearest(Near, standIns);

                Parts(at).ShouldBe(new[] { 0f, 0f, 300f });
            }

            [Fact]
            public void Gives_the_nearest_of_several_stand_ins()
            {
                var standIns = new List<StandInSpot>
                {
                    Joiner,
                    Joiner with
                    {
                        At = new Vec3(0f, 0f, 100f),
                        Distance = 100f,
                    },
                };

                var at = ThreatRules.Nearest(Near, standIns);

                Parts(at).ShouldBe(new[] { 0f, 0f, 100f });
            }

            [Fact]
            public void Skips_a_dead_stand_in()
            {
                var dead = Joiner with { Alive = false };
                var standIns = new List<StandInSpot> { dead };

                var at = ThreatRules.Nearest(Near, standIns);

                Parts(at).ShouldBe(new[] { 500f, 0f, 0f });
            }

            [Fact]
            public void Keeps_the_host_on_a_tie()
            {
                var tie = Joiner with { Distance = 500f };
                var standIns = new List<StandInSpot> { tie };

                var at = ThreatRules.Nearest(Near, standIns);

                Parts(at).ShouldBe(new[] { 500f, 0f, 0f });
            }

            [Fact]
            public void Gives_the_host_when_off()
            {
                var near = Near with { On = false };
                var standIns = new List<StandInSpot> { Joiner };

                var at = ThreatRules.Nearest(near, standIns);

                Parts(at).ShouldBe(new[] { 500f, 0f, 0f });
            }

            [Fact]
            public void Gives_the_host_with_no_enemy()
            {
                var near = Near with { Exists = false };
                var standIns = new List<StandInSpot> { Joiner };

                var at = ThreatRules.Nearest(near, standIns);

                Parts(at).ShouldBe(new[] { 500f, 0f, 0f });
            }

            [Fact]
            public void Gives_the_enemy_s_own_spot_with_no_host()
            {
                var standIns = new List<StandInSpot> { Joiner };
                var near = Near with
                {
                    HasHost = false,
                    EnemyAt = new Vec3(7f, 8f, 9f),
                };

                var at = ThreatRules.Nearest(near, standIns);

                Parts(at).ShouldBe(new[] { 7f, 8f, 9f });
            }

            [Fact]
            public void Gives_the_origin_with_no_host_and_no_enemy()
            {
                var standIns = new List<StandInSpot> { Joiner };
                var near = Near with
                {
                    HasHost = false,
                    Exists = false,
                    EnemyAt = new Vec3(7f, 8f, 9f),
                };

                var at = ThreatRules.Nearest(near, standIns);

                Parts(at).ShouldBe(new[] { 0f, 0f, 0f });
            }
        }

        public class FleesFromTarget
        {
            static readonly EnemyFacts Chaser = Hunter with
            {
                HasTarget = true,
                TargetIsPlayer = true,
            };

            [Fact]
            public void Flees_from_a_player_it_is_after()
            {
                ThreatRules.FleesFromTarget(Chaser).ShouldBeTrue();
            }

            [Fact]
            public void Flees_from_the_nearest_with_no_target()
            {
                var enemy = Chaser with { HasTarget = false };

                ThreatRules.FleesFromTarget(enemy).ShouldBeFalse();
            }

            [Fact]
            public void Flees_from_the_nearest_when_after_something_else()
            {
                var enemy = Chaser with { TargetIsPlayer = false };

                ThreatRules.FleesFromTarget(enemy).ShouldBeFalse();
            }

            [Fact]
            public void Flees_from_the_nearest_when_off()
            {
                var enemy = Chaser with { On = false };

                ThreatRules.FleesFromTarget(enemy).ShouldBeFalse();
            }

            [Fact]
            public void Flees_from_the_nearest_with_no_enemy()
            {
                var enemy = Chaser with { Exists = false };

                ThreatRules.FleesFromTarget(enemy).ShouldBeFalse();
            }
        }
    }
}
