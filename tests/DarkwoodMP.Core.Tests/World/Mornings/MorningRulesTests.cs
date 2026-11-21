using DarkwoodMP.World.Mornings;

namespace DarkwoodMP.Tests.World.Mornings
{
    public partial class MorningRulesTests
    {
        /// The host's morning, frozen, with a joiner out of theirs.
        static readonly ClockFacts JoinerOut = new ClockFacts
        {
            Frozen = true,
            Now = new Seconds(100f),
            InCoop = true,
            HasSync = true,
            AnyoneOut = true,
        };

        /// Frozen since exactly the grace ago, the clock still stopped.
        static readonly ClockState Stopped = new ClockState
        {
            Freed = false,
            Timing = true,
            Since = new Seconds(95f),
        };

        static readonly ClockState Freed = Stopped with { Freed = true };

        static readonly HostEndFacts HostEnd = new HostEndFacts
        {
            IsHost = true,
            InWorld = true,
            Replaying = false,
            HasController = true,
            Peers = 1,
        };

        static readonly WalkOutFacts WalkOut = new WalkOutFacts
        {
            Before = true,
            HasController = true,
            After = false,
            IsClient = true,
            InWorld = true,
            Replaying = false,
        };

        /// The host in its own morning, not talking, a joiner walked out.
        static readonly LeftFacts Left = new LeftFacts
        {
            IsHost = true,
            FromPeer = true,
            InWorld = true,
            HasController = true,
            InMorning = true,
            Dreaming = false,
            Talking = false,
        };

        /// A client in its morning, not talking, told by the host.
        static readonly EndedFacts Ended = new EndedFacts
        {
            IsClient = true,
            FromHost = true,
            InWorld = true,
            Pending = false,
            HasController = true,
            InMorning = true,
            Dreaming = false,
            Talking = false,
        };

        static readonly VisitorFacts Visitor = new VisitorFacts
        {
            HasPlayer = true,
            HasWindow = true,
            HasNpc = true,
            InDialogue = true,
            HasLocations = true,
            AtBase = true,
        };

        /// An end waiting while we still talk to the trader.
        static readonly PendingFacts Waiting = new PendingFacts
        {
            Pending = true,
            HasController = true,
            HasWindow = true,
            HasPlayer = true,
            InWorld = true,
            Talking = true,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_grace_the_telling_and_the_close_timeout()
            {
                var limits = new[]
                {
                    MorningRules.Grace,
                    MorningRules.TellEvery,
                    MorningRules.CloseTimeout,
                };

                limits.ShouldBe(new[] { 5f, 3f, 10f });
            }
        }

        public class Clock
        {
            [Fact]
            public void Runs_the_clock_exactly_at_the_grace()
            {
                var step = MorningRules.Clock(JoinerOut, Stopped);

                step.Change.ShouldBe(ClockChange.Run);
                step.Clock.ShouldBe(Freed);
            }

            [Fact]
            public void Waits_just_before_the_grace()
            {
                var old = Stopped with { Since = new Seconds(95.5f) };

                var step = MorningRules.Clock(JoinerOut, old);

                step.Change.ShouldBe(ClockChange.None);
                step.Clock.ShouldBe(old);
            }

            [Fact]
            public void Starts_timing_on_the_first_frozen_frame()
            {
                var step = MorningRules.Clock(JoinerOut, default);

                step.Change.ShouldBe(ClockChange.None);
                step.Clock.Timing.ShouldBeTrue();
                step.Clock.Since.Value.ShouldBe(100f);
            }

            [Fact]
            public void Stops_the_clock_once_everyone_is_back_in()
            {
                var clock = JoinerOut with { AnyoneOut = false };

                var step = MorningRules.Clock(clock, Freed);

                step.Change.ShouldBe(ClockChange.Stop);
                step.Clock.ShouldBe(Stopped);
            }

            [Fact]
            public void Leaves_a_freed_clock_running()
            {
                var step = MorningRules.Clock(JoinerOut, Freed);

                step.Change.ShouldBe(ClockChange.None);
                step.Clock.ShouldBe(Freed);
            }

            [Fact]
            public void Forgets_without_touching_the_clock_when_not_frozen()
            {
                var clock = JoinerOut with { Frozen = false };

                var step = MorningRules.Clock(clock, Freed);

                step.ShouldBe(default(ClockStep));
            }

            [Fact]
            public void Stops_a_freed_clock_when_coop_ends()
            {
                var clock = JoinerOut with { InCoop = false };

                var step = MorningRules.Clock(clock, Freed);

                step.Change.ShouldBe(ClockChange.Stop);
            }

            [Fact]
            public void Waits_without_player_sync()
            {
                var clock = JoinerOut with { HasSync = false };

                var step = MorningRules.Clock(clock, Stopped);

                step.Change.ShouldBe(ClockChange.None);
            }
        }

        public class IsOut
        {
            [Theory]
            [InlineData(false, true, false, true)]
            [InlineData(true, true, false, false)]
            [InlineData(false, false, false, false)]
            [InlineData(false, true, true, false)]
            public void Counts_a_real_player_out_of_their_morning(
                bool dummy,
                bool state,
                bool morning,
                bool outside)
            {
                MorningRules.IsOut(dummy, state, morning).ShouldBe(outside);
            }
        }

        public class EndedHere
        {
            [Theory]
            [InlineData(true, false, false, true)]
            [InlineData(true, false, true, false)]
            [InlineData(false, false, false, false)]
            [InlineData(true, true, false, false)]
            public void Sends_an_end_but_not_the_traders_death(
                bool before,
                bool after,
                bool killed,
                bool sent)
            {
                MorningRules.EndedHere(before, after, killed).ShouldBe(sent);
            }
        }

        public class HostEnded
        {
            [Fact]
            public void Marks_the_day_and_tells_everyone()
            {
                var end = MorningRules.HostEnded(HostEnd);

                end.MarkOver.ShouldBeTrue();
                end.Send.ShouldBeTrue();
            }

            [Fact]
            public void Marks_the_day_alone_without_sending()
            {
                var end = MorningRules.HostEnded(HostEnd with { Peers = 0 });

                end.MarkOver.ShouldBeTrue();
                end.Send.ShouldBeFalse();
            }

            [Fact]
            public void Sends_without_a_controller_to_mark()
            {
                var facts = HostEnd with { HasController = false };

                var end = MorningRules.HostEnded(facts);

                end.MarkOver.ShouldBeFalse();
                end.Send.ShouldBeTrue();
            }

            [Fact]
            public void Does_nothing_while_replaying()
            {
                var facts = HostEnd with { Replaying = true };

                MorningRules.HostEnded(facts).ShouldBe(default(HostEndStep));
            }

            [Fact]
            public void Does_nothing_on_a_client()
            {
                var facts = HostEnd with { IsHost = false };

                MorningRules.HostEnded(facts).ShouldBe(default(HostEndStep));
            }

            [Fact]
            public void Does_nothing_out_of_a_world()
            {
                var facts = HostEnd with { InWorld = false };

                MorningRules.HostEnded(facts).ShouldBe(default(HostEndStep));
            }
        }

        public class WalkedOut
        {
            [Fact]
            public void Tells_the_host_when_a_trigger_exit_ended_it()
            {
                MorningRules.WalkedOut(WalkOut).ShouldBeTrue();
            }

            [Fact]
            public void Stays_quiet_when_the_morning_goes_on()
            {
                var walk = WalkOut with { After = true };

                MorningRules.WalkedOut(walk).ShouldBeFalse();
            }

            [Fact]
            public void Stays_quiet_out_of_a_morning()
            {
                var walk = WalkOut with { Before = false };

                MorningRules.WalkedOut(walk).ShouldBeFalse();
            }

            [Fact]
            public void Stays_quiet_on_the_host()
            {
                var walk = WalkOut with { IsClient = false };

                MorningRules.WalkedOut(walk).ShouldBeFalse();
            }

            [Fact]
            public void Stays_quiet_while_replaying()
            {
                var walk = WalkOut with { Replaying = true };

                MorningRules.WalkedOut(walk).ShouldBeFalse();
            }

            [Fact]
            public void Stays_quiet_out_of_a_world()
            {
                var walk = WalkOut with { InWorld = false };

                MorningRules.WalkedOut(walk).ShouldBeFalse();
            }

            [Fact]
            public void Stays_quiet_without_a_controller()
            {
                var walk = WalkOut with { HasController = false };

                MorningRules.WalkedOut(walk).ShouldBeFalse();
            }
        }
    }
}
