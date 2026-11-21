using DarkwoodMP.World.Inventories;

namespace DarkwoodMP.Tests.World.Inventories
{
    public class WorkbenchLevelRulesTests
    {
        /// A client at a workbench raised it from 2 to 3.
        static readonly LevelFacts Upgrade = new LevelFacts
        {
            Level = 3,
            Current = 2,
            AtWorkbench = true,
        };

        public class Raises
        {
            [Theory]
            [InlineData(3, true)]
            [InlineData(2, false)]
            [InlineData(1, false)]
            public void Takes_only_a_level_above_ours(int level, bool expected)
            {
                var raises = WorkbenchLevelRules.Raises(level, 2);

                raises.ShouldBe(expected);
            }
        }

        public class MayRaise
        {
            [Fact]
            public void Allows_one_level_up_at_a_workbench()
            {
                var verdict = WorkbenchLevelRules.MayRaise(Upgrade);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_level_more_than_one_up()
            {
                var skip = Upgrade with { Level = 4 };

                var verdict = WorkbenchLevelRules.MayRaise(skip);

                verdict.ShouldBe(Verdict.Refuse(
                    Refusal.LevelNotNext,
                    Refusals.Detail("4", "2")));
            }

            [Fact]
            public void Refuses_a_player_away_from_a_workbench()
            {
                var away = Upgrade with { AtWorkbench = false };

                var verdict = WorkbenchLevelRules.MayRaise(away);

                verdict.Reason.ShouldBe(Refusal.NotAtWorkbench);
            }

            [Fact]
            public void Says_the_level_before_the_workbench()
            {
                var both = Upgrade with { Level = 5, AtWorkbench = false };

                var verdict = WorkbenchLevelRules.MayRaise(both);

                verdict.Reason.ShouldBe(Refusal.LevelNotNext);
            }
        }
    }
}
