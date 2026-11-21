using DarkwoodMP.World.Culling;

namespace DarkwoodMP.Tests.World.Culling
{
    public class CullingRulesTests
    {
        /// The host's refresh with a joiner in the world.
        static readonly RefreshFacts Shared = new RefreshFacts
        {
            Grid = true,
            StandIns = true,
            Instant = false,
            Session = true,
        };

        /// A 1920 x 1080 screen at modifiers 1 and zoom 1.
        static readonly ScreenFacts Screen = new ScreenFacts
        {
            Width = 1920,
            Height = 1080,
            WidthModifier = 1f,
            HeightModifier = 1f,
            Zoom = 1f,
        };

        /// The host is being taken to a place, and the timer is due.
        static readonly LoadingFacts Loading = new LoadingFacts
        {
            StandIns = true,
            Loading = true,
            Refreshes = true,
            Player = true,
            LoadingGame = false,
            WorldMade = true,
            Now = new Seconds(10f),
            Next = new Seconds(10f),
        };

        static readonly ViewSize Area = new ViewSize { Dx = 100, Dy = 50 };

        public class Which
        {
            [Fact]
            public void Shares_the_refresh_with_stand_ins()
            {
                CullingRules.Which(Shared).ShouldBe(RefreshKind.Shared);
            }

            [Fact]
            public void Leaves_a_grid_less_refresh_to_the_game()
            {
                var refresh = Shared with { Grid = false };

                CullingRules.Which(refresh).ShouldBe(RefreshKind.Game);
            }

            [Fact]
            public void Runs_its_own_pass_alone_in_a_session()
            {
                var refresh = Shared with { StandIns = false };

                CullingRules.Which(refresh).ShouldBe(RefreshKind.OwnPass);
            }

            [Theory]
            [InlineData(true, true)]
            [InlineData(false, false)]
            public void Leaves_an_instant_or_sessionless_one_to_the_game(
                bool instant, bool session)
            {
                var refresh = Shared with
                {
                    StandIns = false,
                    Instant = instant,
                    Session = session,
                };

                CullingRules.Which(refresh).ShouldBe(RefreshKind.Game);
            }

            [Fact]
            public void Shares_an_instant_one_with_stand_ins()
            {
                var refresh = Shared with { Instant = true, Session = false };

                CullingRules.Which(refresh).ShouldBe(RefreshKind.Shared);
            }
        }

        public class HostArea
        {
            [Fact]
            public void Adds_500_to_the_screen()
            {
                var area = CullingRules.HostArea(Screen);

                area.ShouldBe(new ViewSize { Dx = 2420, Dy = 1580 });
            }

            [Fact]
            public void Takes_the_larger_modifier_and_the_zoom()
            {
                var screen = Screen with { HeightModifier = 2f, Zoom = 2f };

                var area = CullingRules.HostArea(screen);

                area.ShouldBe(new ViewSize { Dx = 1460, Dy = 1040 });
            }

            [Fact]
            public void Cuts_to_whole_units()
            {
                var screen = Screen with { Zoom = 3f };

                var area = CullingRules.HostArea(screen);

                area.ShouldBe(new ViewSize { Dx = 806, Dy = 526 });
            }
        }

        public class Near
        {
            [Theory]
            [InlineData(100f, 50f, true)]
            [InlineData(-100f, -50f, true)]
            [InlineData(100.5f, 0f, false)]
            [InlineData(0f, 50.5f, false)]
            public void Takes_the_edges_of_the_area(
                float x, float z, bool expected)
            {
                var node = new Vec2(x, z);

                var near = CullingRules.Near(node, new Vec2(0f, 0f), Area);

                near.ShouldBe(expected);
            }
        }

        public class GridOf
        {
            [Theory]
            [InlineData(true, true, PlayerGrid.Location)]
            [InlineData(false, true, PlayerGrid.None)]
            [InlineData(false, false, PlayerGrid.World)]
            public void Picks_the_location_before_away(
                bool inLocation, bool away, PlayerGrid expected)
            {
                CullingRules.GridOf(inLocation, away).ShouldBe(expected);
            }
        }

        public class HostAway
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(true, true, false)]
            [InlineData(false, false, false)]
            public void Is_away_when_the_world_isn_t_current(
                bool world, bool current, bool expected)
            {
                CullingRules.HostAway(world, current).ShouldBe(expected);
            }
        }

        public class Enters
        {
            [Theory]
            [InlineData(true, true, true)]
            [InlineData(false, false, true)]
            [InlineData(false, true, false)]
            public void Enters_a_new_node_or_any_when_forced(
                bool force, bool entered, bool expected)
            {
                CullingRules.Enters(force, entered).ShouldBe(expected);
            }
        }

        public class WhileLoading
        {
            [Fact]
            public void Pins_the_period()
            {
                CullingRules.LoadingEvery.ShouldBe(0.5f);
            }

            [Fact]
            public void Runs_when_due_and_waits_half_a_second()
            {
                var tick = CullingRules.WhileLoading(Loading);

                tick.Due.ShouldBeTrue();
                tick.Next.Value.ShouldBe(10.5f);
            }

            [Fact]
            public void Waits_before_the_time()
            {
                var load = Loading with { Now = new Seconds(9.9f) };

                var tick = CullingRules.WhileLoading(load);

                tick.Due.ShouldBeFalse();
                tick.Next.Value.ShouldBe(10f);
            }

            public static TheoryData<LoadingFacts> Closed => new()
            {
                Loading with { StandIns = false },
                Loading with { Loading = false },
                Loading with { Refreshes = false },
                Loading with { Player = false },
                Loading with { LoadingGame = true },
                Loading with { WorldMade = false },
            };

            [Theory]
            [MemberData(nameof(Closed))]
            public void Leaves_the_timer_while_a_gate_is_shut(LoadingFacts load)
            {
                var tick = CullingRules.WhileLoading(load);

                tick.Due.ShouldBeFalse();
                tick.Next.Value.ShouldBe(10f);
            }
        }
    }
}
