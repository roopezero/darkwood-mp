using DarkwoodMP.World.Workbenches;

namespace DarkwoodMP.Tests.World.Workbenches
{
    public class WorkbenchRulesTests
    {
        /// Rows given at level 2 in scene 7.
        static readonly RowsApplied Given = new RowsApplied
        {
            Scene = 7,
            Level = 2,
        };

        /// The same world, ready, a frame later.
        static readonly RowsFacts Same = new RowsFacts
        {
            Ready = true,
            Scene = 7,
            Level = 2,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_row_and_level_limits()
            {
                var limits = new[]
                {
                    WorkbenchRules.RowSize,
                    WorkbenchRules.BaseLevel,
                    WorkbenchRules.FallbackBase,
                    WorkbenchRules.WideFrom,
                };

                limits.ShouldBe(new[] { 6, 1, 30, 30 });
            }

            [Fact]
            public void Starts_with_no_scene_and_no_level()
            {
                var none = RowsApplied.None;

                new[] { none.Scene, none.Level }.ShouldBe(new[] { -1, -1 });
            }
        }

        public class Wanted
        {
            [Theory]
            [InlineData(30, 1, 30)]
            [InlineData(30, 2, 36)]
            [InlineData(30, 5, 54)]
            [InlineData(24, 3, 36)]
            public void Adds_a_row_for_each_level_above_the_first(
                int baseSlots,
                int level,
                int wanted)
            {
                var slots = WorkbenchRules.Wanted(baseSlots, level);

                slots.ShouldBe(wanted);
            }

            [Theory]
            [InlineData(0)]
            [InlineData(-4)]
            public void Adds_nothing_for_a_level_below_the_first(int level)
            {
                var slots = WorkbenchRules.Wanted(30, level);

                slots.ShouldBe(30);
            }
        }

        public class Grows
        {
            [Fact]
            public void Grows_a_storage_one_slot_short()
            {
                WorkbenchRules.Grows(35, 36).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_a_storage_exactly_at_the_count()
            {
                WorkbenchRules.Grows(36, 36).ShouldBeFalse();
            }

            [Fact]
            public void Never_shrinks_a_bigger_storage()
            {
                WorkbenchRules.Grows(42, 36).ShouldBeFalse();
            }
        }

        public class Wide
        {
            [Fact]
            public void Widens_a_storage_of_exactly_thirty_slots()
            {
                WorkbenchRules.Wide(30).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_a_storage_just_under_thirty_slots()
            {
                WorkbenchRules.Wide(29).ShouldBeFalse();
            }
        }

        public class Update
        {
            [Fact]
            public void Does_nothing_while_nothing_changed()
            {
                var update = WorkbenchRules.Update(Given, Same);

                update.Why.ShouldBe(RowsWhy.None);
            }

            [Fact]
            public void Keeps_the_state_while_nothing_changed()
            {
                var update = WorkbenchRules.Update(Given, Same);

                update.Applied.ShouldBe(Given);
            }

            [Fact]
            public void Gives_the_rows_when_the_level_changes()
            {
                var now = Same with { Level = 3 };

                var update = WorkbenchRules.Update(Given, now);

                update.Why.ShouldBe(RowsWhy.LevelChanged);
            }

            [Fact]
            public void Remembers_the_new_level()
            {
                var now = Same with { Level = 3 };

                var update = WorkbenchRules.Update(Given, now);

                update.Applied.ShouldBe(Given with { Level = 3 });
            }

            [Fact]
            public void Gives_the_rows_in_a_new_scene()
            {
                var now = Same with { Scene = 8 };

                var update = WorkbenchRules.Update(Given, now);

                update.Why.ShouldBe(RowsWhy.WorldLoaded);
            }

            [Fact]
            public void Counts_a_new_scene_and_level_as_loaded()
            {
                var now = Same with { Scene = 8, Level = 3 };

                var update = WorkbenchRules.Update(Given, now);

                update.Why.ShouldBe(RowsWhy.WorldLoaded);
            }

            [Fact]
            public void Gives_the_rows_in_the_first_world()
            {
                var update = WorkbenchRules.Update(RowsApplied.None, Same);

                update.Why.ShouldBe(RowsWhy.WorldLoaded);
            }

            [Fact]
            public void Does_nothing_while_no_world_is_ready()
            {
                var now = Same with { Ready = false, Level = 3 };

                var update = WorkbenchRules.Update(Given, now);

                update.Why.ShouldBe(RowsWhy.None);
            }

            [Fact]
            public void Forgets_only_the_scene_while_not_ready()
            {
                var now = Same with { Ready = false, Level = 3 };

                var update = WorkbenchRules.Update(Given, now);

                update.Applied.ShouldBe(Given with { Scene = -1 });
            }

            [Fact]
            public void Counts_the_same_scene_as_loaded_after_a_gap()
            {
                var gap = Given with { Scene = RowsApplied.NoScene };

                var update = WorkbenchRules.Update(gap, Same);

                update.Why.ShouldBe(RowsWhy.WorldLoaded);
            }
        }
    }
}
