using DarkwoodMP.Combat.Damage;

namespace DarkwoodMP.Tests.Combat.Damage
{
    public class AreaHitRulesTests
    {
        /// An enemy's flame touching a living stand-in on the host.
        static readonly FlameTouchFacts Burn = new FlameTouchFacts
        {
            Active = true,
            Hosting = true,
            StandIn = true,
            Alive = true,
            ByEnemy = true,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_area_hit_limits()
            {
                AreaHitRules.MostSpawned.ShouldBe(512);
                AreaHitRules.DiveDamage.ShouldBe(5f);
                AreaHitRules.LeastShotDistance.ShouldBe(1f);
            }
        }

        public class Hosting
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(false, false, false)]
            [InlineData(true, true, false)]
            public void Hosts_with_stand_ins_and_not_as_a_client(
                bool standIns, bool client, bool expected)
            {
                AreaHitRules.Hosting(standIns, client).ShouldBe(expected);
            }
        }

        public class Sets
        {
            [Theory]
            [InlineData(1, 0, true, SpawnSets.Enemy)]
            [InlineData(1, 0, false, SpawnSets.None)]
            [InlineData(0, 1, false, SpawnSets.Player)]
            [InlineData(0, 0, true, SpawnSets.None)]
            [InlineData(2, 1, true, SpawnSets.Enemy | SpawnSets.Player)]
            public void Puts_it_in_the_sets_of_the_spawns_it_is_in(
                int enemy, int player, bool hosting, SpawnSets expected)
            {
                var sets = AreaHitRules.Sets(enemy, player, hosting);

                sets.ShouldBe(expected);
            }
        }

        public class Prunes
        {
            [Theory]
            [InlineData(512, false)]
            [InlineData(513, true)]
            public void Prunes_a_set_over_512(int count, bool expected)
            {
                AreaHitRules.Prunes(count).ShouldBe(expected);
            }
        }

        public class ByEnemy
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(false, true, true)]
            [InlineData(false, false, false)]
            public void Counts_an_enemy_spawner_or_the_enemy_set(
                bool spawner, bool inSet, bool expected)
            {
                AreaHitRules.ByEnemy(spawner, inSet).ShouldBe(expected);
            }
        }

        public class Living
        {
            [Theory]
            [InlineData(true, false, false, true)]
            [InlineData(true, true, true, true)]
            [InlineData(true, true, false, false)]
            [InlineData(false, false, false, false)]
            public void Counts_a_living_stand_in_whose_collider_is_on(
                bool alive, bool collider, bool enabled, bool expected)
            {
                var living = AreaHitRules.Living(alive, collider, enabled);

                living.ShouldBe(expected);
            }
        }

        public class AuraRuns
        {
            [Theory]
            [InlineData(true, true, 100f, true)]
            [InlineData(true, true, 0f, false)]
            [InlineData(true, true, float.NaN, true)]
            [InlineData(false, true, 100f, false)]
            [InlineData(true, false, 100f, false)]
            public void Runs_a_living_enemy_s_aura_with_a_range(
                bool alive, bool hosting, float range, bool expected)
            {
                var runs = AreaHitRules.AuraRuns(alive, hosting, range);

                runs.ShouldBe(expected);
            }
        }

        public class AuraHit
        {
            [Fact]
            public void Hits_at_the_centre_for_half_the_damage()
            {
                var hits = AreaHitRules.AuraHit(40f, 100f, 0f, out var hit);

                hits.ShouldBeTrue();
                hit.ShouldBe(20f);
            }

            [Theory]
            [InlineData(49f, true)]
            [InlineData(50f, false)]
            [InlineData(100f, false)]
            [InlineData(101f, false)]
            public void Hits_only_within_half_the_range(float d, bool hits)
            {
                var hit = AreaHitRules.AuraHit(40f, 100f, d, out _);

                hit.ShouldBe(hits);
            }

            [Fact]
            public void Falls_off_towards_half_the_range()
            {
                AreaHitRules.AuraHit(40f, 100f, 25f, out var hit);

                hit.ShouldBe(10f);
            }
        }

        public class Dive
        {
            [Theory]
            [InlineData(true, true, FlierTarget.StandIn)]
            [InlineData(true, false, FlierTarget.StandIn)]
            [InlineData(false, true, FlierTarget.OurPlayer)]
            [InlineData(false, false, FlierTarget.Nobody)]
            public void Hits_the_stand_in_it_dove_at_or_our_player(
                bool standIn, bool player, FlierTarget expected)
            {
                AreaHitRules.Dive(standIn, player).ShouldBe(expected);
            }
        }

        public class Shoots
        {
            [Theory]
            [InlineData(true, true, true, true)]
            [InlineData(false, true, true, false)]
            [InlineData(true, false, true, false)]
            [InlineData(true, true, false, false)]
            public void Shoots_for_an_enemy_with_a_target_on_the_host(
                bool target, bool hosting, bool enemy, bool expected)
            {
                var shoots = AreaHitRules.Shoots(target, hosting, enemy);

                shoots.ShouldBe(expected);
            }
        }

        public class ShotHit
        {
            [Theory]
            [InlineData(200f, 2f)]
            [InlineData(1f, 400f)]
            [InlineData(0.5f, 400f)]
            [InlineData(0f, 400f)]
            public void Divides_the_damage_by_the_distance_at_least_1(
                float d, float expected)
            {
                AreaHitRules.ShotHit(400f, d, true, out var hit);

                hit.ShouldBe(expected);
            }

            [Fact]
            public void Misses_a_stand_in_it_can_t_see()
            {
                var hits = AreaHitRules.ShotHit(400f, 10f, false, out _);

                hits.ShouldBeFalse();
            }
        }

        public class FlameHurts
        {
            [Fact]
            public void Burns_a_living_stand_in()
            {
                AreaHitRules.FlameHurts(Burn).ShouldBeTrue();
            }

            [Fact]
            public void Skips_a_flame_that_isn_t_active()
            {
                var touch = Burn with { Active = false };

                AreaHitRules.FlameHurts(touch).ShouldBeFalse();
            }

            [Fact]
            public void Skips_it_unless_hosting()
            {
                var touch = Burn with { Hosting = false };

                AreaHitRules.FlameHurts(touch).ShouldBeFalse();
            }

            [Fact]
            public void Skips_what_is_no_stand_in()
            {
                var touch = Burn with { StandIn = false };

                AreaHitRules.FlameHurts(touch).ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_dead_stand_in()
            {
                var touch = Burn with { Alive = false };

                AreaHitRules.FlameHurts(touch).ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_flame_no_enemy_made()
            {
                var touch = Burn with { ByEnemy = false };

                AreaHitRules.FlameHurts(touch).ShouldBeFalse();
            }
        }

        public class Frozen
        {
            [Theory]
            [InlineData(true, true, true)]
            [InlineData(false, true, false)]
            [InlineData(true, false, false)]
            public void Stops_a_frozen_copy_in_ClientMode(
                bool client, bool frozen, bool expected)
            {
                AreaHitRules.Frozen(client, frozen).ShouldBe(expected);
            }
        }
    }
}
