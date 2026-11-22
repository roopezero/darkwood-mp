using System.Collections.Generic;
using DarkwoodMP.World;

namespace DarkwoodMP.Tests.World
{
    public class InventoryRulesTests
    {
        static Dictionary<string, int> Count(params (string, int)[] items)
        {
            var count = new Dictionary<string, int>();
            foreach (var (type, n) in items)
            {
                count[type] = n;
            }

            return count;
        }

        public class Limits
        {
            [Fact]
            public void Pins_the_least_count()
            {
                InventoryRules.LeastCount.ShouldBe(1);
            }
        }

        public class CountOf
        {
            [Theory]
            [InlineData(0, 1)]
            [InlineData(-3, 1)]
            [InlineData(1, 1)]
            [InlineData(2, 2)]
            [InlineData(5, 5)]
            public void Counts_an_item_as_at_least_one(int amount, int count)
            {
                var counted = InventoryRules.CountOf(amount);

                counted.ShouldBe(count);
            }
        }

        public class Change
        {
            [Fact]
            public void Gives_nothing_without_a_count_before()
            {
                var change = InventoryRules.Change(null, Count(("nail", 3)));

                change.ShouldBeNull();
            }

            [Fact]
            public void Gives_nothing_without_a_count_now()
            {
                var change = InventoryRules.Change(Count(("nail", 3)), null);

                change.ShouldBeNull();
            }

            [Fact]
            public void Gives_nothing_when_nothing_changed()
            {
                var change = InventoryRules.Change(
                    Count(("nail", 3)),
                    Count(("nail", 3)));

                change.ShouldBeNull();
            }

            [Fact]
            public void Gives_now_minus_before_for_a_changed_type()
            {
                var change = InventoryRules.Change(
                    Count(("nail", 3), ("wood", 2)),
                    Count(("nail", 1), ("wood", 2)));

                change.ShouldBe(Count(("nail", -2)));
            }

            [Fact]
            public void Gives_minus_before_for_a_type_gone()
            {
                var change = InventoryRules.Change(
                    Count(("nail", 3), ("wood", 2)),
                    Count(("wood", 2)));

                change.ShouldBe(Count(("nail", -3)));
            }

            [Fact]
            public void Gives_the_count_of_a_new_type()
            {
                var change = InventoryRules.Change(
                    Count(("wood", 2)),
                    Count(("wood", 2), ("rag", 4)));

                change.ShouldBe(Count(("rag", 4)));
            }
        }
    }
}
