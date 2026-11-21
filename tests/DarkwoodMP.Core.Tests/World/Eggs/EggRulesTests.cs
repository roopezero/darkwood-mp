using DarkwoodMP.World;
using DarkwoodMP.World.Eggs;

namespace DarkwoodMP.Tests.World.Eggs
{
    public class EggRulesTests
    {
        /// A client's break of a larva egg exactly at the reach.
        static readonly EggFacts Egg = new EggFacts
        {
            Found = true,
            Destructible = true,
            Name = "larva_egg_big",
            Reach = new ReachFacts
            {
                Handling = true,
                Known = true,
                StandIn = new Vec2(0f, 0f),
                Target = new Vec2(1200f, 0f),
            },
        };

        static EggFacts At(float x)
        {
            return Egg with
            {
                Reach = Egg.Reach with { Target = new Vec2(x, 0f) },
            };
        }

        public class Limits
        {
            [Fact]
            public void Pins_the_find_radius_and_the_reach()
            {
                var limits = new[] { EggRules.FindRadius, EggRules.Reach };

                limits.ShouldBe(new[] { 50f, 1200f });
            }

            [Fact]
            public void Pins_the_word_in_an_eggs_name()
            {
                EggRules.Word.ShouldBe("egg");
            }
        }

        public class IsEgg
        {
            [Theory]
            [InlineData(true, false, "larva_egg_big", true)]
            [InlineData(true, false, "LARVA_EGG", true)]
            [InlineData(true, true, "cocoon", true)]
            [InlineData(true, false, "cocoon", false)]
            [InlineData(false, true, "larva_egg_big", false)]
            public void Tells_an_egg_by_what_it_is_or_its_name(
                bool destructible,
                bool opensByBreaking,
                string name,
                bool egg)
            {
                var found = EggRules.IsEgg(destructible, opensByBreaking, name);

                found.ShouldBe(egg);
            }
        }

        public class Sends
        {
            [Theory]
            [InlineData(false, true, true)]
            [InlineData(true, true, false)]
            [InlineData(false, false, false)]
            public void Sends_only_a_named_egg_outside_a_place_of_our_own(
                bool oursAlone,
                bool named,
                bool sent)
            {
                var sends = EggRules.Sends(oursAlone, named);

                sends.ShouldBe(sent);
            }
        }

        public class SentHealth
        {
            [Theory]
            [InlineData(-3, 0)]
            [InlineData(-1, 0)]
            [InlineData(0, 0)]
            [InlineData(5, 5)]
            public void Puts_a_health_below_0_at_0(int health, int sent)
            {
                var left = EggRules.SentHealth(health);

                left.ShouldBe(sent);
            }
        }

        public class MayBreak
        {
            [Fact]
            public void Allows_an_egg_exactly_at_the_reach()
            {
                var verdict = EggRules.MayBreak(Egg);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_an_egg_just_past_the_reach()
            {
                var verdict = EggRules.MayBreak(At(1200.5f));

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Words_the_reach_refusal_with_the_eggs_name()
            {
                var verdict = EggRules.MayBreak(At(4041f));

                var text = Refusals.Text(verdict);

                text.ShouldBe(
                    "larva_egg_big is 4041 away from them (reach 1200)");
            }

            [Fact]
            public void Refuses_an_egg_that_isnt_here()
            {
                var egg = Egg with { Found = false };

                var verdict = EggRules.MayBreak(egg);

                verdict.Reason.ShouldBe(Refusal.NoSuchEgg);
            }

            [Fact]
            public void Refuses_what_isnt_an_egg_by_its_name()
            {
                var egg = Egg with { Name = "cocoon" };

                var verdict = EggRules.MayBreak(egg);

                verdict.ShouldBe(Verdict.Refuse(Refusal.NotAnEgg, "cocoon"));
            }

            [Fact]
            public void Refuses_an_egg_that_isnt_destructible()
            {
                var egg = Egg with { Destructible = false };

                var verdict = EggRules.MayBreak(egg);

                verdict.Reason.ShouldBe(Refusal.NotAnEgg);
            }

            [Fact]
            public void Allows_an_egg_broken_already_from_any_distance()
            {
                var egg = At(4041f) with { Destroyed = true };

                var verdict = EggRules.MayBreak(egg);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_missing_egg_before_its_kind()
            {
                var egg = Egg with { Found = false, Destructible = false };

                var verdict = EggRules.MayBreak(egg);

                verdict.Reason.ShouldBe(Refusal.NoSuchEgg);
            }

            [Fact]
            public void Refuses_what_isnt_an_egg_before_its_reach()
            {
                var egg = At(4041f) with { Name = "cocoon" };

                var verdict = EggRules.MayBreak(egg);

                verdict.Reason.ShouldBe(Refusal.NotAnEgg);
            }

            [Fact]
            public void Refuses_what_isnt_an_egg_even_if_broken()
            {
                var egg = Egg with { Name = "cocoon", Destroyed = true };

                var verdict = EggRules.MayBreak(egg);

                verdict.Reason.ShouldBe(Refusal.NotAnEgg);
            }
        }

        public class Breaks
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(true, true, false)]
            [InlineData(false, false, false)]
            public void Breaks_only_an_egg_here_and_whole(
                bool found,
                bool destroyed,
                bool breaks)
            {
                var broken = EggRules.Breaks(found, destroyed);

                broken.ShouldBe(breaks);
            }
        }
    }
}
