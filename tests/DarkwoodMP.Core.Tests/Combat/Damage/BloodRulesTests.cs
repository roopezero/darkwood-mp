using DarkwoodMP.Combat.Damage;

namespace DarkwoodMP.Tests.Combat.Damage
{
    public class BloodRulesTests
    {
        /// The host's normal hit takes 10 health.
        static readonly BloodFacts Hit = new BloodFacts
        {
            NormalHit = true,
            Before = 100f,
            After = 90f,
            NightTrader = false,
            Host = true,
        };

        public class Sends
        {
            [Fact]
            public void Sends_a_normal_hit_that_took_health()
            {
                BloodRules.Sends(Hit).ShouldBeTrue();
            }

            [Theory]
            [InlineData(90f, false)]
            [InlineData(90.5f, true)]
            public void Needs_some_health_taken(float before, bool expected)
            {
                var hit = Hit with { Before = before };

                BloodRules.Sends(hit).ShouldBe(expected);
            }

            [Fact]
            public void Skips_a_hit_that_isn_t_normal()
            {
                var hit = Hit with { NormalHit = false };

                BloodRules.Sends(hit).ShouldBeFalse();
            }

            [Fact]
            public void Skips_the_night_trader()
            {
                var hit = Hit with { NightTrader = true };

                BloodRules.Sends(hit).ShouldBeFalse();
            }

            [Fact]
            public void Skips_unless_hosting()
            {
                var hit = Hit with { Host = false };

                BloodRules.Sends(hit).ShouldBeFalse();
            }
        }

        public class Dealt
        {
            [Fact]
            public void Takes_the_health_after_from_before()
            {
                BloodRules.Dealt(Hit).ShouldBe(10f);
            }
        }

        public class Rotation
        {
            [Theory]
            [InlineData(true, 30f)]
            [InlineData(false, 200f)]
            public void Turns_with_the_attacker_when_there_is_one(
                bool attacker, float expected)
            {
                var y = BloodRules.Rotation(attacker, 30f, 200f);

                y.ShouldBe(expected);
            }
        }
    }
}
