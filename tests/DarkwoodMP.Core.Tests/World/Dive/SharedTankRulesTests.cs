using DarkwoodMP.World.Dive;

namespace DarkwoodMP.Tests.World.Dive
{
    public class SharedTankRulesTests
    {
        public class Limits
        {
            [Fact]
            public void Pins_the_item_the_flag_and_the_interval()
            {
                SharedTankRules.Item.ShouldBe("oxygenTank_full");
                SharedTankRules.Flag.ShouldBe("dwmp_oxygenTankFilled");
                SharedTankRules.Interval.ShouldBe(2f);
            }
        }

        public class Due
        {
            [Theory]
            [InlineData(10f, 10f, true)]
            [InlineData(9.9f, 10f, false)]
            [InlineData(0f, 0f, true)]
            public void Looks_once_the_time_comes(
                float now,
                float next,
                bool due)
            {
                var looks = SharedTankRules.Due(
                    new Seconds(now),
                    new Seconds(next));

                looks.ShouldBe(due);
            }
        }

        public class Next
        {
            [Fact]
            public void Looks_again_2_seconds_later()
            {
                var next = SharedTankRules.Next(new Seconds(10f));

                next.Value.ShouldBe(12f);
            }
        }

        public class Looks
        {
            /// A player in a world, the flag not set.
            static readonly WatchFacts World = new WatchFacts
            {
                InWorld = true,
                Player = true,
            };

            [Fact]
            public void Looks_at_a_player_in_a_world()
            {
                var looks = SharedTankRules.Looks(World);

                looks.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_a_tank_filled_already()
            {
                var world = World with { Filled = true };

                var looks = SharedTankRules.Looks(world);

                looks.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_it_out_of_a_world()
            {
                var world = World with { InWorld = false };

                var looks = SharedTankRules.Looks(world);

                looks.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_it_while_loading()
            {
                var world = World with { Loading = true };

                var looks = SharedTankRules.Looks(world);

                looks.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_it_in_the_main_menu()
            {
                var world = World with { MainMenu = true };

                var looks = SharedTankRules.Looks(world);

                looks.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_it_with_no_player()
            {
                var world = World with { Player = false };

                var looks = SharedTankRules.Looks(world);

                looks.ShouldBeFalse();
            }
        }

        public class Sets
        {
            [Theory]
            [InlineData(true, false, true, true)]
            [InlineData(false, true, true, true)]
            [InlineData(false, false, true, false)]
            [InlineData(true, true, false, false)]
            public void Sets_the_flag_for_a_tank_in_the_bags_or_journal(
                bool inBags,
                bool inJournal,
                bool flags,
                bool sets)
            {
                var player = new HoldFacts
                {
                    InBags = inBags,
                    InJournal = inJournal,
                    Flags = flags,
                };

                var set = SharedTankRules.Sets(player);

                set.ShouldBe(sets);
            }
        }

        public class Filled
        {
            [Theory]
            [InlineData(true, true, true)]
            [InlineData(true, false, false)]
            [InlineData(false, false, false)]
            public void Reads_the_flag_as_set_when_there_and_true(
                bool found,
                bool isTrue,
                bool filled)
            {
                var set = SharedTankRules.Filled(found, isTrue);

                set.ShouldBe(filled);
            }
        }

        public class IsTank
        {
            [Theory]
            [InlineData("oxygenTank_full", true)]
            [InlineData("oxygenTank_empty", false)]
            [InlineData(null, false)]
            public void Knows_the_full_tank_by_its_type(string type, bool tank)
            {
                var found = SharedTankRules.IsTank(type);

                found.ShouldBe(tank);
            }
        }

        public class Requirement
        {
            /// "Has a full tank", the flag set, and the game said no.
            static readonly RequirementFacts HasTank = new RequirementFacts
            {
                HaveItem = true,
                Tank = true,
                Filled = true,
                ActiveModifier = true,
            };

            [Fact]
            public void Passes_has_a_tank_once_filled()
            {
                var met = SharedTankRules.Requirement(HasTank);

                met.ShouldBeTrue();
            }

            [Fact]
            public void Fails_no_tank_once_filled()
            {
                var need = HasTank with
                {
                    ActiveModifier = false,
                    Result = true,
                };

                var met = SharedTankRules.Requirement(need);

                met.ShouldBeFalse();
            }

            [Fact]
            public void Keeps_the_games_result_before_the_fill()
            {
                var need = HasTank with { Filled = false };

                var met = SharedTankRules.Requirement(need);

                met.ShouldBeFalse();
            }

            [Fact]
            public void Keeps_the_games_result_for_another_item()
            {
                var need = HasTank with { Tank = false, Result = true };

                var met = SharedTankRules.Requirement(need);

                met.ShouldBeTrue();
            }

            [Fact]
            public void Keeps_the_games_result_for_another_requirement()
            {
                var need = HasTank with { HaveItem = false };

                var met = SharedTankRules.Requirement(need);

                met.ShouldBeFalse();
            }
        }
    }
}
