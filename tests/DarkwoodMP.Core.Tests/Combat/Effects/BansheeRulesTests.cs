using DarkwoodMP.Combat.Effects;

namespace DarkwoodMP.Tests.Combat.Effects
{
    public class BansheeRulesTests
    {
        /// The host's banshee in our living player's sight.
        static readonly BansheeFacts Seen = new BansheeFacts
        {
            Banshee = true,
            Away = false,
            Alive = true,
            InSight = true,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_banshee_s_numbers()
            {
                var numbers = new[]
                {
                    BansheeRules.LeastDistance,
                    BansheeRules.ShakeTime,
                    BansheeRules.ShakeAt1,
                    BansheeRules.OverlayAt1,
                    BansheeRules.MostOverlay,
                    BansheeRules.OverlayTime,
                };

                numbers.ShouldBe(new[] { 1f, 0.5f, 1200f, 70f, 0.5f, 0.5f });
                BansheeRules.AgitatedSound.ShouldBe("banshee_agitated_player");
            }
        }

        public class Sees
        {
            [Fact]
            public void Counts_a_banshee_in_sight()
            {
                BansheeRules.Sees(Seen).ShouldBeTrue();
            }

            public static TheoryData<BansheeFacts> Unseen => new()
            {
                Seen with { Banshee = false },
                Seen with { Away = true },
                Seen with { Alive = false },
                Seen with { InSight = false },
            };

            [Theory]
            [MemberData(nameof(Unseen))]
            public void Skips_any_other(BansheeFacts c)
            {
                BansheeRules.Sees(c).ShouldBeFalse();
            }
        }

        public class Distance
        {
            [Theory]
            [InlineData(0f, 1f)]
            [InlineData(1f, 1f)]
            [InlineData(1.5f, 1.5f)]
            public void Takes_at_least_1(float d, float expected)
            {
                BansheeRules.Distance(d).ShouldBe(expected);
            }
        }

        public class Nearest
        {
            [Fact]
            public void Picks_the_first_of_the_nearest()
            {
                var nearest = BansheeRules.Nearest(new[] { 30f, 10f, 10f });

                nearest.ShouldBe(1);
            }

            [Fact]
            public void Picks_none_of_none()
            {
                BansheeRules.Nearest(new float[0]).ShouldBe(-1);
            }

            [Fact]
            public void Picks_none_of_NaN()
            {
                BansheeRules.Nearest(new[] { float.NaN }).ShouldBe(-1);
            }
        }

        public class Shake
        {
            [Fact]
            public void Shakes_less_further_away()
            {
                BansheeRules.Shake(100f).ShouldBe(12f);
            }
        }

        public class Overlay
        {
            [Theory]
            [InlineData(1f, 0.5f)]
            [InlineData(140f, 0.5f)]
            [InlineData(280f, 0.25f)]
            public void Fades_with_the_distance_up_to_half(
                float d, float expected)
            {
                BansheeRules.Overlay(d).ShouldBe(expected);
            }
        }
    }
}
