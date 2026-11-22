using DarkwoodMP.Combat.Damage;

namespace DarkwoodMP.Tests.Combat.Damage
{
    public class ExplosionRulesTests
    {
        /// A bomb of 100 damage and radius 200, 50 from a stand-in it sees.
        static readonly BlastFacts Near = new BlastFacts
        {
            Radius = 200f,
            Damage = 100f,
            Distance = 50f,
            Sees = true,
        };

        /// An enemy's bomb with a sound, the host with one joiner.
        static readonly ExplosionSendFacts Bomb = new ExplosionSendFacts
        {
            ByEnemy = true,
            Host = true,
            Peers = 1,
            Sound = true,
            Effect = false,
        };

        public class Handles
        {
            [Theory]
            [InlineData(true, true, true)]
            [InlineData(false, true, false)]
            [InlineData(true, false, false)]
            public void Handles_an_explosion_with_an_effect_on_the_host(
                bool effect, bool hosting, bool expected)
            {
                ExplosionRules.Handles(effect, hosting).ShouldBe(expected);
            }
        }

        public class HurtsStandIns
        {
            [Theory]
            [InlineData(true, 1f, true)]
            [InlineData(true, 0f, false)]
            [InlineData(false, 1f, false)]
            public void Hurts_when_it_affects_players_and_has_a_radius(
                bool affects, float radius, bool expected)
            {
                var hurts = ExplosionRules.HurtsStandIns(affects, radius);

                hurts.ShouldBe(expected);
            }
        }

        public class Hits
        {
            [Fact]
            public void Falls_off_with_the_distance()
            {
                var hits = ExplosionRules.Hits(Near, out var hit);

                hits.ShouldBeTrue();
                hit.ShouldBe(75f);
            }

            [Theory]
            [InlineData(199f, true)]
            [InlineData(200f, false)]
            [InlineData(201f, false)]
            public void Misses_at_the_radius(float d, bool expected)
            {
                var blast = Near with { Distance = d };

                ExplosionRules.Hits(blast, out _).ShouldBe(expected);
            }

            [Fact]
            public void Misses_at_the_radius_whatever_the_damage()
            {
                var blast = Near with
                {
                    Distance = 200f,
                    Damage = float.PositiveInfinity,
                };

                ExplosionRules.Hits(blast, out _).ShouldBeFalse();
            }

            [Fact]
            public void Misses_a_stand_in_out_of_sight()
            {
                var blast = Near with { Sees = false };

                ExplosionRules.Hits(blast, out _).ShouldBeFalse();
            }

            [Theory]
            [InlineData(0f)]
            [InlineData(-10f)]
            public void Misses_for_no_damage(float damage)
            {
                var blast = Near with { Damage = damage };

                ExplosionRules.Hits(blast, out _).ShouldBeFalse();
            }

            [Fact]
            public void Sends_a_NaN_hit_as_today()
            {
                var blast = Near with { Damage = float.NaN };

                var hits = ExplosionRules.Hits(blast, out var hit);

                hits.ShouldBeTrue();
                float.IsNaN(hit).ShouldBeTrue();
            }
        }

        public class Sends
        {
            [Fact]
            public void Sends_an_enemy_s_bomb_to_the_clients()
            {
                ExplosionRules.Sends(Bomb).ShouldBeTrue();
            }

            [Fact]
            public void Sends_one_with_only_an_effect()
            {
                var blast = Bomb with { Sound = false, Effect = true };

                ExplosionRules.Sends(blast).ShouldBeTrue();
            }

            [Fact]
            public void Skips_one_with_nothing_to_show()
            {
                var blast = Bomb with { Sound = false };

                ExplosionRules.Sends(blast).ShouldBeFalse();
            }

            [Fact]
            public void Skips_one_no_enemy_made()
            {
                var blast = Bomb with { ByEnemy = false };

                ExplosionRules.Sends(blast).ShouldBeFalse();
            }

            [Fact]
            public void Skips_it_unless_hosting()
            {
                var blast = Bomb with { Host = false };

                ExplosionRules.Sends(blast).ShouldBeFalse();
            }

            [Fact]
            public void Skips_it_with_no_peers()
            {
                var blast = Bomb with { Peers = 0 };

                ExplosionRules.Sends(blast).ShouldBeFalse();
            }
        }
    }
}
