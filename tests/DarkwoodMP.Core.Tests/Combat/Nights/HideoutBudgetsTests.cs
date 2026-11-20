using DarkwoodMP.Combat.Nights;

namespace DarkwoodMP.Tests.Combat.Nights
{
    public class HideoutBudgetsTests
    {
        /// Two dogs sent at hideout 7.
        static HideoutBudgets TwoDogs()
        {
            var budgets = new HideoutBudgets();
            budgets.Spend(7, "dog");
            budgets.Spend(7, "dog");
            return budgets;
        }

        public class Out
        {
            [Fact]
            public void Counts_none_at_a_hideout_never_sent_to()
            {
                new HideoutBudgets().Out(7, "dog").ShouldBe(0);
            }

            [Fact]
            public void Counts_each_kind_at_each_hideout_apart()
            {
                var budgets = TwoDogs();
                budgets.Spend(9, "dog");
                budgets.Spend(7, "banshee");

                budgets.Out(7, "dog").ShouldBe(2);
                budgets.Out(9, "dog").ShouldBe(1);
                budgets.Out(7, "banshee").ShouldBe(1);
                budgets.Out(9, "banshee").ShouldBe(0);
            }
        }

        public class Gone
        {
            [Fact]
            public void Counts_one_fewer()
            {
                var budgets = TwoDogs();

                budgets.Gone(7, "dog");

                budgets.Out(7, "dog").ShouldBe(1);
            }

            [Fact]
            public void Never_counts_below_zero()
            {
                var budgets = TwoDogs();

                budgets.Gone(7, "dog");
                budgets.Gone(7, "dog");
                budgets.Gone(7, "dog");

                budgets.Out(7, "dog").ShouldBe(0);
            }

            [Fact]
            public void Leaves_a_kind_never_sent_alone()
            {
                var budgets = TwoDogs();

                budgets.Gone(7, "banshee");
                budgets.Gone(9, "dog");

                budgets.Out(7, "dog").ShouldBe(2);
                budgets.Out(7, "banshee").ShouldBe(0);
            }
        }

        public class Clear
        {
            [Fact]
            public void Starts_every_budget_again()
            {
                var budgets = TwoDogs();

                budgets.Clear();

                budgets.Out(7, "dog").ShouldBe(0);
            }
        }
    }
}
