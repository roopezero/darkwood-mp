using System.Linq;
using DarkwoodMP.World.Inventories;

namespace DarkwoodMP.Tests.World.Inventories
{
    public class SortRulesTests
    {
        /// Planks, ten to a stack, in the first category.
        static readonly SortStack Plank = new SortStack
        {
            Type = "plank",
            Amount = 4,
            Durability = 1f,
            HasBase = true,
            Stackable = true,
            MaxAmount = 10,
            MaxDurability = 1f,
            Category = 1,
        };

        /// A can of gasoline whose fuel is its durability, 100 at most.
        static readonly SortStack Can = new SortStack
        {
            Type = "gasoline",
            Amount = 1,
            Durability = 60f,
            HasBase = true,
            StacksDurability = true,
            MaxAmount = 1,
            MaxDurability = 100f,
            Category = 2,
        };

        /// A client asks to sort a storage it may change.
        static readonly SortFacts Asked = new SortFacts { StorageHere = true };

        /// A client presses Sort & Stack with nothing held, in a world.
        static readonly SortPressFacts Press = new SortPressFacts
        {
            IsClient = true,
            InWorld = true,
            Named = true,
        };

        static SortedStack[] Sorted(params SortStack[] stacks)
        {
            return SortRules.Sort(stacks).ToArray();
        }

        public class Limits
        {
            [Fact]
            public void Pins_no_category_and_no_fuel()
            {
                var limits = new[] { SortRules.NoCategory, SortRules.NoFuel };

                limits.ShouldBe(new[] { 10000f, 0.001f });
            }
        }

        public class Pressed
        {
            [Fact]
            public void Predicts_and_asks_on_a_client()
            {
                var press = SortRules.Pressed(Press);

                press.ShouldBe(SortPress.PredictAndAsk);
            }

            [Fact]
            public void Does_nothing_with_an_item_on_the_cursor()
            {
                var held = Press with { Holding = true, IsClient = false };

                var press = SortRules.Pressed(held);

                press.ShouldBe(SortPress.Nothing);
            }

            [Fact]
            public void Sorts_at_once_on_the_host()
            {
                var host = Press with { IsClient = false };

                var press = SortRules.Pressed(host);

                press.ShouldBe(SortPress.Sort);
            }

            [Fact]
            public void Sorts_at_once_outside_a_world()
            {
                var menu = Press with { InWorld = false, Named = false };

                var press = SortRules.Pressed(menu);

                press.ShouldBe(SortPress.Sort);
            }

            [Fact]
            public void Warns_of_a_storage_with_no_name()
            {
                var nameless = Press with { Named = false, Waiting = true };

                var press = SortRules.Pressed(nameless);

                press.ShouldBe(SortPress.NoName);
            }

            [Fact]
            public void Waits_for_the_hosts_contents()
            {
                var waiting = Press with { Waiting = true };

                var press = SortRules.Pressed(waiting);

                press.ShouldBe(SortPress.StillWaiting);
            }
        }

        public class SendsSorted
        {
            [Theory]
            [InlineData(true, 1, true, true)]
            [InlineData(true, 0, true, false)]
            [InlineData(false, 1, true, false)]
            [InlineData(true, 1, false, false)]
            public void Sends_the_hosts_changed_slots(
                bool host,
                int changed,
                bool named,
                bool expected)
            {
                var sends = SortRules.SendsSorted(host, changed, named);

                sends.ShouldBe(expected);
            }
        }

        public class MaySort
        {
            [Fact]
            public void Allows_a_storage_the_client_may_change()
            {
                var verdict = SortRules.MaySort(Asked);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_storage_that_isnt_here()
            {
                var gone = Asked with
                {
                    StorageHere = false,
                    Container = Verdict.Refuse(Refusal.TooFar),
                };

                var verdict = SortRules.MaySort(gone);

                verdict.Reason.ShouldBe(Refusal.NoSuchStorage);
            }

            [Fact]
            public void Refuses_what_the_container_refuses()
            {
                var far = Asked with
                {
                    Container = Verdict.Refuse(Refusal.TooFar),
                    HostMoving = true,
                };

                var verdict = SortRules.MaySort(far);

                verdict.Reason.ShouldBe(Refusal.TooFar);
            }

            [Fact]
            public void Refuses_while_the_host_moves_an_item_in_it()
            {
                var moving = Asked with { HostMoving = true };

                var verdict = SortRules.MaySort(moving);

                verdict.Reason.ShouldBe(Refusal.HostMovingItem);
            }
        }

        public class Sort
        {
            [Fact]
            public void Merges_a_partial_stack_into_an_earlier_one()
            {
                var later = Plank with { Slot = 1, Amount = 3 };

                var sorted = Sorted(Plank, later);

                sorted.Single().Amount.ShouldBe(7);
            }

            [Fact]
            public void Averages_durability_by_amount_when_merging()
            {
                var later = Plank with
                {
                    Slot = 1,
                    Amount = 3,
                    Durability = 0.5f,
                };

                var sorted = Sorted(Plank, later);

                sorted.Single().Durability.ShouldBe(5.5f / 7f, 0.0001f);
            }

            [Fact]
            public void Tops_up_to_a_full_stack_and_leaves_the_rest()
            {
                var first = Plank with { Amount = 8 };
                var later = Plank with { Slot = 1, Amount = 5 };

                var sorted = Sorted(first, later);

                sorted.Select(s => s.Amount).ShouldBe(new[] { 10, 3 });
            }

            [Fact]
            public void Leaves_a_full_stack_as_it_is()
            {
                var full = Plank with { Amount = 10 };
                var later = Plank with { Slot = 1, Amount = 3 };

                var sorted = Sorted(full, later);

                sorted.Select(s => s.Amount).ShouldBe(new[] { 10, 3 });
            }

            public static TheoryData<SortStack> Unmergeable()
            {
                var later = Plank with { Slot = 1, Amount = 3 };
                return new TheoryData<SortStack>
                {
                    later with { Upgrades = 1 },
                    later with { Modifiers = 1 },
                    later with { HasAmmo = true },
                    later with { Stackable = false },
                    later with { MaxAmount = 1 },
                    later with { HasBase = false },
                    later with { Recipe = true, RecipeFor = "x" },
                };
            }

            [Theory]
            [MemberData(nameof(Unmergeable))]
            public void Never_merges_an_item_that_doesnt_stack(SortStack odd)
            {
                var sorted = Sorted(Plank, odd);

                sorted.Length.ShouldBe(2);
            }

            [Fact]
            public void Pours_a_can_into_an_earlier_one()
            {
                var later = Can with { Slot = 1, Durability = 70f };

                var sorted = Sorted(Can, later);

                sorted.Select(s => s.Durability)
                    .ShouldBe(new[] { 100f, 30f });
            }

            [Fact]
            public void Drops_a_can_poured_empty()
            {
                var later = Can with { Slot = 1, Durability = 40f };

                var sorted = Sorted(Can, later);

                sorted.Single().Durability.ShouldBe(100f);
            }

            [Fact]
            public void Keeps_an_empty_item_the_game_keeps()
            {
                var lantern = Can with { Repairable = true };
                var later = lantern with { Slot = 1, Durability = 40f };

                var sorted = Sorted(lantern, later);

                sorted.Length.ShouldBe(2);
            }

            [Theory]
            [InlineData(0.001f, 0)]
            [InlineData(0.002f, 1)]
            public void Drops_a_can_with_no_fuel_left(float fuel, int left)
            {
                var empty = Can with { Durability = fuel };

                var sorted = Sorted(empty);

                sorted.Length.ShouldBe(left);
            }

            [Fact]
            public void Pours_only_into_the_same_type()
            {
                var other = Can with { Slot = 1, Type = "kerosene" };

                var sorted = Sorted(Can, other);

                sorted.Select(s => s.Durability)
                    .ShouldBe(new[] { 60f, 60f });
            }

            [Fact]
            public void Puts_lower_categories_first()
            {
                var can = Can with { Slot = 0 };
                var plank = Plank with { Slot = 1 };

                var sorted = Sorted(can, plank);

                sorted.Select(s => s.From).ShouldBe(new[] { 1, 0 });
            }

            [Fact]
            public void Puts_items_with_no_category_last()
            {
                var none = Can with { Category = SortRules.NoCategory };
                var plank = Plank with { Slot = 1, Category = 9999 };

                var sorted = Sorted(none, plank);

                sorted.Select(s => s.From).ShouldBe(new[] { 1, 0 });
            }

            [Fact]
            public void Orders_types_by_ordinal()
            {
                var lower = Plank with { Type = "axe", MaxAmount = 1 };
                var upper = Plank with
                {
                    Slot = 1,
                    Type = "Zip",
                    MaxAmount = 1,
                };

                var sorted = Sorted(lower, upper);

                sorted.Select(s => s.From).ShouldBe(new[] { 1, 0 });
            }

            [Fact]
            public void Orders_a_recipe_by_what_it_makes_after_items()
            {
                var recipe = Plank with
                {
                    Type = "recipe",
                    Recipe = true,
                    RecipeFor = "axe",
                };

                var item = Plank with { Slot = 1, Type = "zz" };

                var sorted = Sorted(recipe, item);

                sorted.Select(s => s.From).ShouldBe(new[] { 1, 0 });
            }

            [Fact]
            public void Puts_the_fullest_stack_first()
            {
                var few = Plank with { MaxAmount = 1, Amount = 1 };
                var many = Plank with { Slot = 1, MaxAmount = 1, Amount = 2 };

                var sorted = Sorted(few, many);

                sorted.Select(s => s.From).ShouldBe(new[] { 1, 0 });
            }

            [Fact]
            public void Puts_the_most_durable_first_at_equal_amounts()
            {
                var worn = Plank with { MaxAmount = 1, Durability = 0.5f };
                var fresh = Plank with { Slot = 1, MaxAmount = 1 };

                var sorted = Sorted(worn, fresh);

                sorted.Select(s => s.From).ShouldBe(new[] { 1, 0 });
            }

            [Fact]
            public void Keeps_the_order_of_equal_items()
            {
                var a = Plank with { Slot = 2, MaxAmount = 1 };
                var b = Plank with { Slot = 5, MaxAmount = 1 };

                var sorted = Sorted(b, a);

                sorted.Select(s => s.From).ShouldBe(new[] { 2, 5 });
            }
        }
    }
}
