using DarkwoodMP.World.Joining;

namespace DarkwoodMP.Tests.World.Joining
{
    public class GameStateRulesTests
    {
        /// Playing a loaded world, no menu.
        static readonly InWorldFacts Playing = new InWorldFacts
        {
            Profile = true,
            Player = true,
            Made = true,
        };

        public class PauseMenuInWorld
        {
            [Theory]
            [InlineData(true, true, true, true)]
            [InlineData(false, true, true, false)]
            [InlineData(true, false, true, false)]
            [InlineData(true, true, false, false)]
            public void Tells_the_pause_menu_over_a_world(
                bool menu,
                bool generator,
                bool made,
                bool pause)
            {
                GameStateRules.PauseMenuInWorld(menu, generator, made)
                    .ShouldBe(pause);
            }
        }

        public class InWorld
        {
            [Fact]
            public void Counts_a_world_being_played()
            {
                GameStateRules.InWorld(Playing).ShouldBeTrue();
            }

            [Fact]
            public void Counts_the_pause_menu_over_one()
            {
                var inWorld = GameStateRules.InWorld(Playing with
                {
                    Menu = true,
                    PauseMenu = true,
                });

                inWorld.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_out_the_main_menu()
            {
                var inWorld = GameStateRules.InWorld(Playing with
                {
                    Menu = true,
                });

                inWorld.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_game_without_a_profile()
            {
                var inWorld = GameStateRules.InWorld(Playing with
                {
                    Profile = false,
                });

                inWorld.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_game_without_a_player()
            {
                var inWorld = GameStateRules.InWorld(Playing with
                {
                    Player = false,
                });

                inWorld.ShouldBeFalse();
            }
        }

        public class LooksReady
        {
            [Fact]
            public void Looks_ready_when_played()
            {
                GameStateRules.LooksReady(Playing).ShouldBeTrue();
            }

            [Fact]
            public void Waits_for_the_world_to_be_made()
            {
                var ready = GameStateRules.LooksReady(Playing with
                {
                    Made = false,
                });

                ready.ShouldBeFalse();
            }

            [Fact]
            public void Waits_while_loading()
            {
                var ready = GameStateRules.LooksReady(Playing with
                {
                    Loading = true,
                });

                ready.ShouldBeFalse();
            }

            [Fact]
            public void Waits_while_inputs_are_forbidden()
            {
                var ready = GameStateRules.LooksReady(Playing with
                {
                    Forbid = true,
                });

                ready.ShouldBeFalse();
            }
        }

        public class Ready
        {
            [Fact]
            public void Starts_the_count_at_the_first_ready_read()
            {
                var step = GameStateRules.Ready(
                    true,
                    new Seconds(-1f),
                    new Seconds(50f));

                step.Since.Value.ShouldBe(50f);
            }

            [Theory]
            [InlineData(52f, false)]
            [InlineData(52.01f, true)]
            public void Is_ready_after_2_seconds(float now, bool ready)
            {
                var step = GameStateRules.Ready(
                    true,
                    new Seconds(50f),
                    new Seconds(now));

                step.Ready.ShouldBe(ready);
            }

            [Fact]
            public void Starts_over_when_not_ready()
            {
                var step = GameStateRules.Ready(
                    false,
                    new Seconds(50f),
                    new Seconds(90f));

                step.Since.Value.ShouldBe(-1f);
            }
        }
    }
}
