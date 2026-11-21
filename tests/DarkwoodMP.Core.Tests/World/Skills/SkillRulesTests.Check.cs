using DarkwoodMP.World.Skills;
using static DarkwoodMP.Tests.World.Skills.Tiers;

namespace DarkwoodMP.Tests.World.Skills
{
    public partial class SkillRulesTests
    {
        public class Check
        {
            [Fact]
            public void Allows_a_first_skill_with_its_negative_skill()
            {
                var verdict = SkillRules.Check(First);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Allows_a_skill_alone_when_none_is_owed()
            {
                var choice = First with
                {
                    Level = 2,
                    Chosen = Set("a2", "n1"),
                    Negative = "",
                };

                var verdict = SkillRules.Check(choice);

                verdict.Refused.ShouldBeFalse();
            }

            [Fact]
            public void Refuses_what_MayTake_refuses_first()
            {
                var choice = First with { Good = "nope", Negative = "" };

                var verdict = SkillRules.Check(choice);

                verdict.Reason.ShouldBe(Refusal.NoSuchSkill);
            }

            [Theory]
            [InlineData(null)]
            [InlineData("")]
            public void Refuses_a_first_skill_without_its_negative_skill(
                string negative)
            {
                var choice = First with { Negative = negative };

                var verdict = SkillRules.Check(choice);

                verdict.Reason.ShouldBe(Refusal.NeedsNegative);
            }

            [Fact]
            public void Refuses_a_negative_skill_none_is_owed()
            {
                var choice = First with { Level = 2, Chosen = Set("a2") };

                var verdict = SkillRules.Check(choice);

                verdict.Reason.ShouldBe(Refusal.NoNegativeNeeded);
            }

            [Fact]
            public void Refuses_what_MayTakeNegative_refuses_last()
            {
                var choice = First with { Negative = "m1" };

                var verdict = SkillRules.Check(choice);

                verdict.ShouldBe(Verdict.Refuse(Refusal.TierClosed, "3"));
            }
        }

        public class MayLearn
        {
            [Theory]
            [InlineData(false, false, SkillNote.NotInWorld)]
            [InlineData(false, true, SkillNote.NotInWorld)]
            [InlineData(true, true, SkillNote.Waiting)]
            [InlineData(true, false, SkillNote.None)]
            public void Learns_only_in_a_world_with_nothing_waiting(
                bool inWorld,
                bool waiting,
                SkillNote note)
            {
                var learn = SkillRules.MayLearn(inWorld, waiting);

                learn.ShouldBe(note);
            }
        }

        public class Handles
        {
            [Theory]
            [InlineData(true, true, true)]
            [InlineData(true, false, false)]
            [InlineData(false, true, false)]
            public void Handles_a_spend_only_as_the_host_in_a_request(
                bool isHost,
                bool handling,
                bool handles)
            {
                var looks = SkillRules.Handles(isHost, handling);

                looks.ShouldBe(handles);
            }
        }

        public class MaySpend
        {
            static readonly SpendFacts Spend = new SpendFacts
            {
                Choice = First,
                Most = 1,
            };

            [Fact]
            public void Allows_a_choice_within_the_shared_levels()
            {
                var verdict = SkillRules.MaySpend(Spend);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_when_the_host_has_no_tiers()
            {
                var spend = Spend with { Choice = First with { Tiers = null } };

                var verdict = SkillRules.MaySpend(spend);

                verdict.Reason.ShouldBe(Refusal.HostNotInWorld);
            }

            [Fact]
            public void Refuses_a_level_one_past_the_most()
            {
                var spend = Spend with { Choice = First with { Level = 2 } };

                var verdict = SkillRules.MaySpend(spend);

                verdict.ShouldBe(
                    Verdict.Refuse(
                        Refusal.LevelTooHigh,
                        Refusals.Detail("2", "1")));
            }

            [Fact]
            public void Refuses_no_tiers_before_the_level()
            {
                var spend = new SpendFacts
                {
                    Choice = First with { Tiers = null, Level = 9 },
                };

                var verdict = SkillRules.MaySpend(spend);

                verdict.Reason.ShouldBe(Refusal.HostNotInWorld);
            }

            [Fact]
            public void Refuses_the_level_before_the_games_rules()
            {
                var spend = Spend with
                {
                    Choice = First with { Level = 2, Good = "nope" },
                };

                var verdict = SkillRules.MaySpend(spend);

                verdict.Reason.ShouldBe(Refusal.LevelTooHigh);
            }

            [Fact]
            public void Refuses_what_Check_refuses()
            {
                var spend = Spend with
                {
                    Choice = First with { Negative = "" },
                };

                var verdict = SkillRules.MaySpend(spend);

                verdict.Reason.ShouldBe(Refusal.NeedsNegative);
            }
        }
    }
}
