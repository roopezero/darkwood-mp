using DarkwoodMP.World.Progress;

namespace DarkwoodMP.Tests.World.Progress
{
    public class StoveRulesTests
    {
        public class Limits
        {
            [Fact]
            public void Pins_the_saturation_a_level_gives()
            {
                StoveRules.Saturation.ShouldBe(5f);
            }

            [Fact]
            public void Pins_the_first_dream()
            {
                StoveRules.FirstDream.ShouldBe("dream_bunker_underground_01");
            }

            [Fact]
            public void Pins_the_first_dreams_level()
            {
                StoveRules.FirstDreamLevel.ShouldBe(2);
            }
        }

        public class RunsGame
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(false, true, true)]
            [InlineData(false, false, false)]
            public void Lets_the_game_show_its_tiers_or_its_open_choice(
                bool onlyShow,
                bool opened,
                bool runs)
            {
                var game = StoveRules.RunsGame(onlyShow, opened);

                game.ShouldBe(runs);
            }
        }

        public class BringsDream
        {
            [Theory]
            [InlineData(2)]
            [InlineData(3)]
            [InlineData(5)]
            [InlineData(6)]
            [InlineData(7)]
            public void Brings_a_dream_at_the_dream_levels(int level)
            {
                var dream = StoveRules.BringsDream(level, false, false);

                dream.ShouldBeTrue();
            }

            [Theory]
            [InlineData(0)]
            [InlineData(1)]
            [InlineData(4)]
            [InlineData(8)]
            public void Brings_none_at_other_levels(int level)
            {
                var dream = StoveRules.BringsDream(level, false, false);

                dream.ShouldBeFalse();
            }

            [Fact]
            public void Brings_a_levels_dream_once()
            {
                var dream = StoveRules.BringsDream(3, true, false);

                dream.ShouldBeFalse();
            }

            [Fact]
            public void Brings_none_with_dreams_disabled()
            {
                var dream = StoveRules.BringsDream(3, false, true);

                dream.ShouldBeFalse();
            }
        }

        public class Transitions
        {
            [Theory]
            [InlineData(true, false, false, true)]
            [InlineData(true, true, true, true)]
            [InlineData(true, true, false, false)]
            [InlineData(false, false, true, false)]
            public void Starts_a_wanted_dream_that_has_a_preset(
                bool wanted,
                bool random,
                bool left,
                bool starts)
            {
                var transition = StoveRules.Transitions(wanted, random, left);

                transition.ShouldBe(starts);
            }
        }

        public class OverlayOpen
        {
            [Theory]
            [InlineData(true, false, true, true)]
            [InlineData(false, false, true, false)]
            [InlineData(true, true, true, false)]
            [InlineData(true, false, false, false)]
            public void Opens_at_the_stove_with_no_menu_over_it(
                bool inWorld,
                bool menuOver,
                bool here,
                bool open)
            {
                var overlay = StoveRules.OverlayOpen(inWorld, menuOver, here);

                overlay.ShouldBe(open);
            }
        }

        public class LevelsToTake
        {
            [Theory]
            [InlineData(true, 4, 1, 3)]
            [InlineData(true, 1, 1, 0)]
            [InlineData(true, 1, 3, 0)]
            [InlineData(false, 4, 1, 0)]
            public void Counts_the_shared_levels_not_taken(
                bool sharing,
                int shared,
                int own,
                int levels)
            {
                var left = StoveRules.LevelsToTake(sharing, shared, own);

                left.ShouldBe(levels);
            }
        }

        public class OverlayShows
        {
            [Theory]
            [InlineData(1, 0, false, true)]
            [InlineData(0, 1, false, true)]
            [InlineData(0, 0, true, true)]
            [InlineData(0, 0, false, false)]
            public void Shows_when_there_is_something_to_do(
                int levels,
                int points,
                bool morning,
                bool shows)
            {
                var overlay = StoveRules.OverlayShows(levels, points, morning);

                overlay.ShouldBe(shows);
            }
        }
    }
}
