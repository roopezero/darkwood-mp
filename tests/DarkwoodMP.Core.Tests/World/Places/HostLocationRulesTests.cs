using DarkwoodMP.World.Places;

namespace DarkwoodMP.Tests.World.Places
{
    public class HostLocationRulesTests
    {
        /// One name queued, the game idle and awake.
        static readonly StartFacts Idle = new StartFacts
        {
            Queued = 1,
            Loading = false,
            Dreaming = false,
            DreamPrepared = false,
            SwitchingDream = false,
        };

        /// Our player walks out of a kept, entered location.
        static readonly LeaveFacts WalkOut = new LeaveFacts
        {
            Kept = true,
            Force = false,
            Entered = true,
            LoadingGame = false,
            Dreaming = false,
            WantToDream = false,
        };

        /// Our player walks into a location entered for a joiner.
        static readonly EnterFacts WalkIn = new EnterFacts
        {
            Force = false,
            Entered = true,
            Kept = true,
            Quiet = false,
        };

        /// Bunker, loading for the joiners, joined its grid.
        static readonly GridFacts Joined = new GridFacts
        {
            Loading = "Bunker",
            HasLocation = true,
            TrueName = "Bunker",
            Before = "World",
            HasGrid = true,
            Grid = "Bunker",
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_load_timeout_and_the_keep_period()
            {
                var limits = new[]
                {
                    HostLocationRules.LoadTimeout,
                    HostLocationRules.KeepEvery,
                };

                limits.ShouldBe(new[] { 60f, 0.5f });
            }
        }

        public class Loading
        {
            static readonly Seconds Began = new Seconds(100f);

            [Fact]
            public void Finishes_once_loaded()
            {
                var now = new Seconds(500f);

                var step = HostLocationRules.Loading(true, now, Began);

                step.ShouldBe(LoadStep.Loaded);
            }

            [Fact]
            public void Waits_exactly_at_the_timeout()
            {
                var now = new Seconds(160f);

                var step = HostLocationRules.Loading(false, now, Began);

                step.ShouldBe(LoadStep.Wait);
            }

            [Fact]
            public void Gives_up_just_past_the_timeout()
            {
                var now = new Seconds(160.01f);

                var step = HostLocationRules.Loading(false, now, Began);

                step.ShouldBe(LoadStep.GaveUp);
            }
        }

        public class MayStart
        {
            [Fact]
            public void Starts_when_idle()
            {
                HostLocationRules.MayStart(Idle).ShouldBeTrue();
            }

            public static TheoryData<StartFacts> Busy =>
                new TheoryData<StartFacts>
                {
                    Idle with { Queued = 0 },
                    Idle with { Loading = true },
                    Idle with { Dreaming = true },
                    Idle with { DreamPrepared = true },
                    Idle with { SwitchingDream = true },
                };

            [Theory]
            [MemberData(nameof(Busy))]
            public void Waits_while_busy(StartFacts start)
            {
                HostLocationRules.MayStart(start).ShouldBeFalse();
            }

            [Theory]
            [InlineData(false, true)]
            [InlineData(true, false)]
            public void Skips_a_name_loaded_already(bool loaded, bool loads)
            {
                HostLocationRules.MayLoad(loaded).ShouldBe(loads);
            }
        }

        public class KeepDue
        {
            static readonly Seconds Next = new Seconds(10f);

            [Fact]
            public void Looks_exactly_when_due()
            {
                var now = new Seconds(10f);

                HostLocationRules.KeepDue(now, Next).ShouldBeTrue();
            }

            [Fact]
            public void Waits_just_before()
            {
                var now = new Seconds(9.99f);

                HostLocationRules.KeepDue(now, Next).ShouldBeFalse();
            }

            [Fact]
            public void Looks_again_half_a_second_later()
            {
                var next = HostLocationRules.NextKeep(new Seconds(10f));

                next.Value.ShouldBe(10.5f);
            }
        }

        public class Wanted
        {
            [Theory]
            [InlineData(true, true, false, true)]
            [InlineData(false, true, false, false)]
            [InlineData(true, false, false, false)]
            [InlineData(true, true, true, false)]
            public void Wants_a_joiners_location_but_the_dream(
                bool proxy,
                bool location,
                bool dream,
                bool wanted)
            {
                var wants = HostLocationRules.Wanted(proxy, location, dream);

                wants.ShouldBe(wanted);
            }
        }

        public class EnterFor
        {
            [Theory]
            [InlineData(true, true, EnterStep.None)]
            [InlineData(false, true, EnterStep.Enter)]
            [InlineData(true, false, EnterStep.ForceEnter)]
            [InlineData(false, false, EnterStep.ForceEnter)]
            public void Enters_what_isnt_running(
                bool entered,
                bool active,
                EnterStep step)
            {
                HostLocationRules.EnterFor(entered, active).ShouldBe(step);
            }
        }

        public class LeavesUnwanted
        {
            [Theory]
            [InlineData(false, true)]
            [InlineData(true, false)]
            public void Leaves_it_unless_ours(bool ours, bool leaves)
            {
                HostLocationRules.LeavesUnwanted(ours).ShouldBe(leaves);
            }
        }

        public class BeforeLeave
        {
            [Fact]
            public void Runs_the_exit_events_but_stays()
            {
                var step = HostLocationRules.BeforeLeave(WalkOut);

                step.ShouldBe(LeaveStep.StayWithExitEvents);
            }

            [Fact]
            public void Stays_even_with_force()
            {
                var leave = WalkOut with { Force = true };

                HostLocationRules.BeforeLeave(leave).ShouldBe(LeaveStep.Stay);
            }

            [Fact]
            public void Leaves_one_not_kept()
            {
                var leave = WalkOut with { Kept = false, Force = true };

                HostLocationRules.BeforeLeave(leave).ShouldBe(LeaveStep.Leave);
            }

            public static TheoryData<LeaveFacts> Quiet =>
                new TheoryData<LeaveFacts>
                {
                    WalkOut with { Entered = false },
                    WalkOut with { LoadingGame = true },
                    WalkOut with { Dreaming = true },
                    WalkOut with { WantToDream = true },
                };

            [Theory]
            [MemberData(nameof(Quiet))]
            public void Stays_without_exit_events(LeaveFacts leave)
            {
                HostLocationRules.BeforeLeave(leave).ShouldBe(LeaveStep.Stay);
            }
        }

        public class RunsEnterEvents
        {
            [Fact]
            public void Runs_them_for_our_player()
            {
                HostLocationRules.RunsEnterEvents(WalkIn).ShouldBeTrue();
            }

            public static TheoryData<EnterFacts> Not =>
                new TheoryData<EnterFacts>
                {
                    WalkIn with { Force = true },
                    WalkIn with { Entered = false },
                    WalkIn with { Kept = false },
                    WalkIn with { Quiet = true },
                };

            [Theory]
            [MemberData(nameof(Not))]
            public void Leaves_them_to_the_game(EnterFacts enter)
            {
                HostLocationRules.RunsEnterEvents(enter).ShouldBeFalse();
            }

            [Theory]
            [InlineData(true, false)]
            [InlineData(false, true)]
            public void Skips_the_games_once_when_quiet(bool quiet, bool runs)
            {
                HostLocationRules.RunsGameEnterEvents(quiet).ShouldBe(runs);
            }
        }

        public class SavesAsLeft
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(true, true, false)]
            [InlineData(false, false, false)]
            public void Saves_a_kept_location_as_left(
                bool kept,
                bool ours,
                bool left)
            {
                HostLocationRules.SavesAsLeft(kept, ours).ShouldBe(left);
            }
        }

        public class PutsGridBack
        {
            [Fact]
            public void Puts_our_grid_back()
            {
                HostLocationRules.PutsGridBack(Joined).ShouldBeTrue();
            }

            public static TheoryData<GridFacts> Not =>
                new TheoryData<GridFacts>
                {
                    Joined with { Loading = null },
                    Joined with { HasLocation = false },
                    Joined with { Before = null },
                    Joined with { TrueName = "Church" },
                    Joined with { HasGrid = false },
                    Joined with { Grid = "World" },
                };

            [Theory]
            [MemberData(nameof(Not))]
            public void Leaves_the_grid_be(GridFacts grid)
            {
                HostLocationRules.PutsGridBack(grid).ShouldBeFalse();
            }
        }
    }
}
