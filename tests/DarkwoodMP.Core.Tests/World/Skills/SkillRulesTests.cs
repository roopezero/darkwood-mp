using DarkwoodMP.World.Skills;
using static DarkwoodMP.Tests.World.Skills.Tiers;

namespace DarkwoodMP.Tests.World.Skills
{
    public partial class SkillRulesTests
    {
        public class Limits
        {
            [Fact]
            public void Pins_two_good_skills_a_tier()
            {
                SkillRules.MostGoodPerTier.ShouldBe(2);
            }
        }

        public class Find
        {
            [Fact]
            public void Finds_a_good_skill_with_its_tier()
            {
                var spot = SkillRules.Find(Three, "b2");

                spot.ShouldBe(new SkillSpot(1, false));
            }

            [Fact]
            public void Finds_a_negative_skill()
            {
                var spot = SkillRules.Find(Three, "m1");

                spot.ShouldBe(new SkillSpot(1, true));
            }

            [Fact]
            public void Takes_the_first_tier_that_has_the_name()
            {
                var tiers = new[]
                {
                    new TierFacts
                    {
                        Good = new string[0],
                        Negative = new[] { "x" },
                    },
                    new TierFacts
                    {
                        Good = new[] { "x" },
                        Negative = new string[0],
                    },
                };

                var spot = SkillRules.Find(tiers, "x");

                spot.ShouldBe(new SkillSpot(0, true));
            }

            [Fact]
            public void Looks_at_a_tiers_good_skills_before_its_negative_ones()
            {
                var tiers = new[]
                {
                    new TierFacts
                    {
                        Good = new[] { "x" },
                        Negative = new[] { "x" },
                    },
                };

                var spot = SkillRules.Find(tiers, "x");

                spot.Negative.ShouldBeFalse();
            }

            [Theory]
            [InlineData(null)]
            [InlineData("")]
            [InlineData("nope")]
            public void Finds_nothing_for_an_unknown_name(string name)
            {
                var spot = SkillRules.Find(Three, name);

                spot.Found.ShouldBeFalse();
            }

            [Fact]
            public void Finds_nothing_without_tiers()
            {
                var spot = SkillRules.Find(null, "a1");

                spot.Tier.ShouldBe(-1);
            }

            [Fact]
            public void Skips_a_tier_without_lists()
            {
                var tiers = new[] { new TierFacts(), Three[0] };

                var spot = SkillRules.Find(tiers, "a1");

                spot.Tier.ShouldBe(1);
            }
        }

        public class TierName
        {
            [Theory]
            [InlineData(0, "I")]
            [InlineData(1, "II")]
            [InlineData(2, "III")]
            [InlineData(3, "IV")]
            [InlineData(4, "5")]
            [InlineData(-1, "0")]
            public void Names_the_tier_as_the_game_does(int index, string name)
            {
                var named = SkillRules.TierName(index);

                named.ShouldBe(name);
            }
        }

        public class Points
        {
            [Theory]
            [InlineData(3, new string[0], 3)]
            [InlineData(3, new[] { "a1", "n1" }, 2)]
            [InlineData(3, new[] { "a1", "a2", "b1" }, 0)]
            [InlineData(1, new[] { "a1", "a2", "b1" }, 0)]
            [InlineData(2, new[] { "nope", "m1" }, 2)]
            public void Counts_the_level_less_the_good_skills(
                int level,
                string[] chosen,
                int points)
            {
                var left = SkillRules.Points(Three, level, Set(chosen));

                left.ShouldBe(points);
            }

            [Fact]
            public void Counts_no_good_skills_without_tiers()
            {
                var left = SkillRules.Points(null, 2, Set("a1"));

                left.ShouldBe(2);
            }
        }

        public class NeedsNegative
        {
            [Fact]
            public void Needs_one_for_a_tiers_first_good_skill()
            {
                var need = SkillRules.NeedsNegative(Three, Set(), "a1");

                need.ShouldBeTrue();
            }

            [Fact]
            public void Needs_none_once_the_tier_has_a_good_skill()
            {
                var need = SkillRules.NeedsNegative(Three, Set("a2"), "a1");

                need.ShouldBeFalse();
            }

            [Fact]
            public void Needs_none_once_the_tier_has_a_negative_skill()
            {
                var need = SkillRules.NeedsNegative(Three, Set("n2"), "a1");

                need.ShouldBeFalse();
            }

            [Fact]
            public void Needs_none_in_a_tier_without_negative_skills()
            {
                var need = SkillRules.NeedsNegative(Three, Set(), "c1");

                need.ShouldBeFalse();
            }

            [Theory]
            [InlineData("n1")]
            [InlineData("nope")]
            public void Needs_none_for_a_name_that_isnt_a_good_skill(
                string name)
            {
                var need = SkillRules.NeedsNegative(Three, Set(), name);

                need.ShouldBeFalse();
            }
        }

        public class MayTake
        {
            [Fact]
            public void Allows_a_good_skill_of_an_open_tier()
            {
                var verdict = SkillRules.MayTake(First);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Theory]
            [InlineData(null)]
            [InlineData("nope")]
            [InlineData("n1")]
            public void Refuses_a_name_that_isnt_a_good_skill(string good)
            {
                var verdict = SkillRules.MayTake(First with { Good = good });

                verdict.Reason.ShouldBe(Refusal.NoSuchSkill);
            }

            [Fact]
            public void Refuses_a_skill_had()
            {
                var choice = First with { Level = 2, Chosen = Set("a1") };

                var verdict = SkillRules.MayTake(choice);

                verdict.Reason.ShouldBe(Refusal.HaveSkill);
            }

            [Fact]
            public void Allows_a_tier_at_its_level()
            {
                var choice = First with { Level = 3, Good = "b1" };

                var verdict = SkillRules.MayTake(choice);

                verdict.Refused.ShouldBeFalse();
            }

            [Fact]
            public void Refuses_a_tier_one_level_early()
            {
                var choice = First with { Level = 2, Good = "b1" };

                var verdict = SkillRules.MayTake(choice);

                verdict.ShouldBe(Verdict.Refuse(Refusal.TierClosed, "3"));
            }

            [Fact]
            public void Allows_a_second_good_skill_of_a_tier()
            {
                var choice = First with { Level = 2, Chosen = Set("a2") };

                var verdict = SkillRules.MayTake(choice);

                verdict.Refused.ShouldBeFalse();
            }

            [Fact]
            public void Refuses_a_third_good_skill_of_a_tier()
            {
                var choice = First with
                {
                    Level = 5,
                    Chosen = Set("a2", "a3"),
                };

                var verdict = SkillRules.MayTake(choice);

                verdict.Reason.ShouldBe(Refusal.TierFull);
            }

            [Fact]
            public void Refuses_with_no_points_left()
            {
                var choice = First with { Level = 1, Chosen = Set("b1") };

                var verdict = SkillRules.MayTake(choice);

                verdict.Reason.ShouldBe(Refusal.NoSkillPoints);
            }

            [Fact]
            public void Refuses_a_skill_had_before_a_closed_tier()
            {
                var choice = First with
                {
                    Level = 0,
                    Chosen = Set("a1"),
                };

                var verdict = SkillRules.MayTake(choice);

                verdict.Reason.ShouldBe(Refusal.HaveSkill);
            }

            [Fact]
            public void Refuses_a_closed_tier_before_a_full_one()
            {
                var choice = First with
                {
                    Level = 0,
                    Chosen = Set("a2", "a3"),
                };

                var verdict = SkillRules.MayTake(choice);

                verdict.Reason.ShouldBe(Refusal.TierClosed);
            }

            [Fact]
            public void Refuses_a_full_tier_before_no_points()
            {
                var choice = First with { Level = 2, Chosen = Set("a2", "a3") };

                var verdict = SkillRules.MayTake(choice);

                verdict.Reason.ShouldBe(Refusal.TierFull);
            }
        }

        public class MayTakeNegative
        {
            [Fact]
            public void Allows_the_negative_skill_a_tier_owes()
            {
                var verdict = SkillRules.MayTakeNegative(First);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Allows_any_owed_negative_skill_without_a_good_one()
            {
                var choice = First with { Good = null, Negative = "n2" };

                var verdict = SkillRules.MayTakeNegative(choice);

                verdict.Refused.ShouldBeFalse();
            }

            [Theory]
            [InlineData(null)]
            [InlineData("nope")]
            [InlineData("a1")]
            public void Refuses_a_name_that_isnt_a_negative_skill(
                string negative)
            {
                var choice = First with { Negative = negative };

                var verdict = SkillRules.MayTakeNegative(choice);

                verdict.Reason.ShouldBe(Refusal.NoSuchNegative);
            }

            [Fact]
            public void Refuses_a_negative_skill_had()
            {
                var choice = First with { Chosen = Set("n1") };

                var verdict = SkillRules.MayTakeNegative(choice);

                verdict.Reason.ShouldBe(Refusal.HaveNegative);
            }

            [Fact]
            public void Refuses_a_tier_one_level_early()
            {
                var choice = First with
                {
                    Level = 2,
                    Good = "b1",
                    Negative = "m1",
                };

                var verdict = SkillRules.MayTakeNegative(choice);

                verdict.ShouldBe(Verdict.Refuse(Refusal.TierClosed, "3"));
            }

            [Fact]
            public void Allows_a_tier_at_its_level()
            {
                var choice = First with
                {
                    Level = 3,
                    Good = "b1",
                    Negative = "m1",
                };

                var verdict = SkillRules.MayTakeNegative(choice);

                verdict.Refused.ShouldBeFalse();
            }

            [Fact]
            public void Refuses_a_tier_that_has_a_good_skill()
            {
                var choice = First with { Level = 2, Chosen = Set("a2") };

                var verdict = SkillRules.MayTakeNegative(choice);

                verdict.Reason.ShouldBe(Refusal.NegativeWithFirst);
            }

            [Fact]
            public void Refuses_a_tier_that_has_its_negative_skill()
            {
                var choice = First with { Chosen = Set("n2") };

                var verdict = SkillRules.MayTakeNegative(choice);

                verdict.Reason.ShouldBe(Refusal.NegativeTaken);
            }

            [Fact]
            public void Refuses_a_good_skill_of_another_tier()
            {
                var choice = First with { Level = 3, Good = "b1" };

                var verdict = SkillRules.MayTakeNegative(choice);

                verdict.Reason.ShouldBe(Refusal.NegativeOtherTier);
            }

            [Theory]
            [InlineData("")]
            [InlineData("nope")]
            public void Refuses_an_unknown_good_skill_as_another_tiers(
                string good)
            {
                var choice = First with { Good = good };

                var verdict = SkillRules.MayTakeNegative(choice);

                verdict.Reason.ShouldBe(Refusal.NegativeOtherTier);
            }

            [Fact]
            public void Refuses_a_skill_had_before_a_closed_tier()
            {
                var choice = First with { Level = 0, Chosen = Set("n1") };

                var verdict = SkillRules.MayTakeNegative(choice);

                verdict.Reason.ShouldBe(Refusal.HaveNegative);
            }

            [Fact]
            public void Refuses_a_closed_tier_before_a_good_skill_had()
            {
                var choice = First with { Level = 0, Chosen = Set("a2") };

                var verdict = SkillRules.MayTakeNegative(choice);

                verdict.Reason.ShouldBe(Refusal.TierClosed);
            }

            [Fact]
            public void Refuses_a_good_skill_had_before_another_tiers()
            {
                var choice = First with
                {
                    Level = 3,
                    Chosen = Set("a2"),
                    Good = "b1",
                };

                var verdict = SkillRules.MayTakeNegative(choice);

                verdict.Reason.ShouldBe(Refusal.NegativeWithFirst);
            }
        }
    }
}
