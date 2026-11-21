using DarkwoodMP.World.Progress;

namespace DarkwoodMP.Tests.World.Progress
{
    public class ProgressRulesTests
    {
        /// Essence 150 at shared level 1, with levels needing 100, 200, 400.
        static readonly ProgressFacts Shared = new ProgressFacts
        {
            SharedExp = 150,
            SharedLevel = 1,
            Requirements = new[] { 100, 200, 400 },
            HasPlayer = true,
        };

        static ProgressNumbers Numbers(int exp, int level) =>
            new ProgressNumbers { Exp = exp, Level = level };

        public class Limits
        {
            [Fact]
            public void Pins_the_last_bar_level()
            {
                ProgressRules.LastBarLevel.ShouldBe(7);
            }

            [Fact]
            public void Pins_the_essence_one_cooking_may_add()
            {
                ProgressRules.LeastEssence.ShouldBe(1);
                ProgressRules.MostEssence.ShouldBe(100000);
            }
        }

        public class Requirement
        {
            [Theory]
            [InlineData(0, 100)]
            [InlineData(2, 400)]
            [InlineData(3, 400)]
            [InlineData(50, 400)]
            [InlineData(-1, 100)]
            public void Clamps_the_level_into_the_list(int level, int need)
            {
                var req = ProgressRules.Requirement(Shared.Requirements, level);

                req.ShouldBe(need);
            }

            [Fact]
            public void Needs_nothing_with_no_list()
            {
                var req = ProgressRules.Requirement(null, 1);

                req.ShouldBe(0);
            }

            [Fact]
            public void Needs_nothing_with_an_empty_list()
            {
                var req = ProgressRules.Requirement(new int[0], 1);

                req.ShouldBe(0);
            }
        }

        public class Display
        {
            [Theory]
            [InlineData(0, 100)]
            [InlineData(1, 150)]
            [InlineData(2, 150)]
            public void Shows_the_shared_essence_up_to_the_next_level(
                int own,
                int shown)
            {
                var exp = ProgressRules.Display(Shared, own);

                exp.ShouldBe(shown);
            }
        }

        public class Allowed
        {
            [Theory]
            [InlineData(99, 0)]
            [InlineData(100, 1)]
            [InlineData(199, 1)]
            [InlineData(200, 2)]
            [InlineData(400, 3)]
            [InlineData(100000, 3)]
            public void Counts_the_levels_the_essence_reaches(
                int exp,
                int levels)
            {
                var allowed = ProgressRules.Allowed(
                    Shared with { SharedExp = exp });

                allowed.ShouldBe(levels);
            }

            [Fact]
            public void Allows_none_with_no_list()
            {
                var allowed = ProgressRules.Allowed(
                    Shared with { Requirements = null });

                allowed.ShouldBe(0);
            }
        }

        public class MostLevels
        {
            [Fact]
            public void Takes_what_the_essence_allows_when_more()
            {
                var most = ProgressRules.MostLevels(
                    Shared with { SharedLevel = 0 });

                most.ShouldBe(1);
            }

            [Fact]
            public void Takes_the_shared_level_when_more()
            {
                var most = ProgressRules.MostLevels(
                    Shared with { SharedLevel = 5 });

                most.ShouldBe(5);
            }

            [Fact]
            public void Takes_the_shared_level_with_no_player()
            {
                var facts = Shared with { SharedLevel = -2, HasPlayer = false };

                var most = ProgressRules.MostLevels(facts);

                most.ShouldBe(-2);
            }
        }

        public class BarFull
        {
            [Fact]
            public void Isnt_full_below_the_levels_need()
            {
                var full = ProgressRules.BarFull(Shared);

                full.ShouldBeFalse();
            }

            [Fact]
            public void Is_full_at_the_levels_need()
            {
                var full = ProgressRules.BarFull(
                    Shared with { SharedExp = 200 });

                full.ShouldBeTrue();
            }

            [Fact]
            public void Reads_the_level_past_the_last_as_the_last()
            {
                var facts = Shared with { SharedExp = 399, SharedLevel = 7 };

                var full = ProgressRules.BarFull(facts);

                full.ShouldBeFalse();
            }

            [Fact]
            public void Is_full_past_the_last_bar_level()
            {
                var facts = Shared with { SharedExp = 0, SharedLevel = 8 };

                var full = ProgressRules.BarFull(facts);

                full.ShouldBeTrue();
            }

            [Fact]
            public void Is_full_with_no_list()
            {
                var full = ProgressRules.BarFull(
                    Shared with { Requirements = null, SharedExp = 0 });

                full.ShouldBeTrue();
            }
        }

        public class Merge
        {
            [Fact]
            public void Takes_the_larger_essence_and_level()
            {
                var merged = ProgressRules.Merge(
                    Numbers(150, 3),
                    Numbers(200, 1));

                merged.ShouldBe(Numbers(200, 3));
            }

            [Fact]
            public void Keeps_the_shared_numbers_when_ours_are_less()
            {
                var merged = ProgressRules.Merge(
                    Numbers(150, 3),
                    Numbers(10, 2));

                merged.ShouldBe(Numbers(150, 3));
            }
        }

        public class Handles
        {
            [Theory]
            [InlineData(true, true, true, true)]
            [InlineData(false, true, true, false)]
            [InlineData(true, false, true, false)]
            [InlineData(true, true, false, false)]
            public void Handles_only_as_the_host_with_a_record_and_a_player(
                bool isHost,
                bool record,
                bool player,
                bool handles)
            {
                var looks = ProgressRules.Handles(isHost, record, player);

                looks.ShouldBe(handles);
            }
        }

        public class MayAddEssence
        {
            [Theory]
            [InlineData(1)]
            [InlineData(100000)]
            public void Allows_an_amount_within_the_limits(int amount)
            {
                var verdict = ProgressRules.MayAddEssence(Shared, amount);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Theory]
            [InlineData(0, "0")]
            [InlineData(-3, "-3")]
            [InlineData(100001, "100001")]
            public void Refuses_an_implausible_amount(int amount, string detail)
            {
                var verdict = ProgressRules.MayAddEssence(Shared, amount);

                verdict.ShouldBe(
                    Verdict.Refuse(Refusal.ImplausibleEssence, detail));
            }

            [Fact]
            public void Refuses_more_on_a_full_bar()
            {
                var verdict = ProgressRules.MayAddEssence(
                    Shared with { SharedExp = 200 },
                    5);

                verdict.Reason.ShouldBe(Refusal.EssenceFull);
            }

            [Fact]
            public void Refuses_the_amount_before_the_full_bar()
            {
                var verdict = ProgressRules.MayAddEssence(
                    Shared with { SharedExp = 200 },
                    0);

                verdict.Reason.ShouldBe(Refusal.ImplausibleEssence);
            }

            [Fact]
            public void Allows_an_amount_past_the_bar()
            {
                var verdict = ProgressRules.MayAddEssence(
                    Shared with { SharedExp = 199 },
                    50000);

                verdict.Refused.ShouldBeFalse();
            }
        }

        public class AcceptedLevel
        {
            [Theory]
            [InlineData(0, 0)]
            [InlineData(1, 1)]
            [InlineData(2, 1)]
            [InlineData(200, 1)]
            public void Caps_the_level_at_what_the_essence_allows(
                int reported,
                int accepted)
            {
                var level = ProgressRules.AcceptedLevel(Shared, reported);

                level.ShouldBe(accepted);
            }
        }

        public class Raises
        {
            [Theory]
            [InlineData(1, false)]
            [InlineData(2, true)]
            [InlineData(0, false)]
            public void Raises_only_past_the_shared_level(
                int level,
                bool raises)
            {
                var raised = ProgressRules.Raises(Shared, level);

                raised.ShouldBe(raises);
            }
        }

        public class Cooked
        {
            [Theory]
            [InlineData(true, false, 1, true)]
            [InlineData(true, false, 0, false)]
            [InlineData(true, false, -5, false)]
            [InlineData(false, false, 5, false)]
            [InlineData(true, true, 5, false)]
            public void Counts_more_essence_from_our_own_player(
                bool player,
                bool replaying,
                int added,
                bool cooked)
            {
                var counts = ProgressRules.Cooked(player, replaying, added);

                counts.ShouldBe(cooked);
            }
        }
    }
}
