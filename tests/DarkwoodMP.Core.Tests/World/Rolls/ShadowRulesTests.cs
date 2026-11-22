using DarkwoodMP.World.Rolls;

namespace DarkwoodMP.Tests.World.Rolls
{
    public class ShadowRulesTests
    {
        static readonly LampFacts Lamp = default;

        static readonly LampFacts Own = new LampFacts { Own = true };

        public class Limits
        {
            [Fact]
            public void Pins_the_flicker_and_the_dark()
            {
                (ShadowRules.FlickerFor, ShadowRules.DarkFor)
                    .ShouldBe((8f, 30f));
            }
        }

        public class Rolls
        {
            [Fact]
            public void Rolls_at_night_with_the_shadows_there()
            {
                ShadowRules.Rolls(false, false, false).ShouldBeTrue();
            }

            [Fact]
            public void Skips_in_the_day()
            {
                ShadowRules.Rolls(false, false, true).ShouldBeFalse();
            }

            [Fact]
            public void Skips_with_the_shadows_removed()
            {
                ShadowRules.Rolls(false, true, false).ShouldBeFalse();
            }

            [Fact]
            public void Rolls_at_any_time_when_asked()
            {
                ShadowRules.Rolls(true, true, true).ShouldBeTrue();
            }
        }

        public class Choices
        {
            [Fact]
            public void Offers_every_lamp_but_the_generators_own()
            {
                var lamps = new[] { Lamp, Own, Lamp };

                ShadowRules.Choices(lamps).ShouldBe(new[] { 0, 2 });
            }

            [Fact]
            public void Leaves_out_only_the_first_own_item()
            {
                var lamps = new[] { Own, Own };

                ShadowRules.Choices(lamps).ShouldBe(new[] { 1 });
            }

            [Fact]
            public void Leaves_out_a_missing_lamp()
            {
                var lamps = new[] { Lamp with { Missing = true }, Lamp };

                ShadowRules.Choices(lamps).ShouldBe(new[] { 1 });
            }

            [Fact]
            public void Leaves_out_a_lamp_kept_dark()
            {
                var lamps = new[] { Lamp, Lamp with { KeptDark = true } };

                ShadowRules.Choices(lamps).ShouldBe(new[] { 0 });
            }

            [Fact]
            public void Offers_none_with_only_the_own_item()
            {
                var lamps = new[] { Own };

                ShadowRules.Choices(lamps).ShouldBeEmpty();
            }

            [Fact]
            public void Offers_none_without_lamps()
            {
                ShadowRules.Choices(new LampFacts[0]).ShouldBeEmpty();
            }
        }
    }
}
