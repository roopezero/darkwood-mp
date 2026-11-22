using DarkwoodMP.World.Clock;

namespace DarkwoodMP.Tests.World.Clock
{
    public class ClockRulesTests
    {
        /// A host in a world with one joiner, its send due exactly now.
        static readonly ClockSendFacts Due = new ClockSendFacts
        {
            IsHost = true,
            Peers = 1,
            InWorld = true,
            HasController = true,
            Now = new Seconds(10f),
            NextSend = new Seconds(10f),
            Minute = 600,
        };

        /// A client at day 3, 10:00, and the host one minute on.
        static readonly ClockFacts OneBehind = new ClockFacts
        {
            HasController = true,
            InWorld = true,
            HostDay = 3,
            HostMinute = 601,
            Day = 3,
            Minute = 600,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_clock_limits()
            {
                var limits = new[]
                {
                    ClockRules.MaxSteps,
                    ClockRules.MaxDaysApart,
                    ClockRules.MinutesPerDay,
                    ClockRules.LastMinute,
                };

                limits.ShouldBe(new[] { 120, 1, 1440, 1439 });
            }

            [Fact]
            public void Pins_the_send_interval()
            {
                ClockRules.SendEvery.ShouldBe(1f);
            }
        }

        public class MaySend
        {
            [Fact]
            public void Sends_exactly_when_it_is_due()
            {
                var sends = ClockRules.MaySend(Due);

                sends.ShouldBeTrue();
            }

            [Fact]
            public void Waits_just_before_it_is_due()
            {
                var clock = Due with { Now = new Seconds(9.99f) };

                var sends = ClockRules.MaySend(clock);

                sends.ShouldBeFalse();
            }

            [Fact]
            public void Sends_nothing_as_a_client()
            {
                var clock = Due with { IsHost = false };

                var sends = ClockRules.MaySend(clock);

                sends.ShouldBeFalse();
            }

            [Fact]
            public void Sends_nothing_with_nobody_joined()
            {
                var clock = Due with { Peers = 0 };

                var sends = ClockRules.MaySend(clock);

                sends.ShouldBeFalse();
            }

            [Fact]
            public void Sends_nothing_outside_a_world()
            {
                var clock = Due with { InWorld = false };

                var sends = ClockRules.MaySend(clock);

                sends.ShouldBeFalse();
            }

            [Fact]
            public void Sends_nothing_with_no_controller()
            {
                var clock = Due with { HasController = false };

                var sends = ClockRules.MaySend(clock);

                sends.ShouldBeFalse();
            }

            [Fact]
            public void Sends_at_once_with_no_send_yet()
            {
                var clock = Due with
                {
                    Now = new Seconds(0f),
                    NextSend = default,
                };

                var sends = ClockRules.MaySend(clock);

                sends.ShouldBeTrue();
            }
        }

        public class NextSend
        {
            [Fact]
            public void Waits_a_second()
            {
                var next = ClockRules.NextSend(new Seconds(10f));

                next.Value.ShouldBe(11f);
            }
        }

        public class MinuteToSend
        {
            [Fact]
            public void Sends_the_clock_minute()
            {
                var minute = ClockRules.MinuteToSend(Due);

                minute.ShouldBe(600);
            }

            [Fact]
            public void Sends_the_clock_minute_as_it_is_unclamped()
            {
                var clock = Due with { Minute = 1500 };

                var minute = ClockRules.MinuteToSend(clock);

                minute.ShouldBe(1500);
            }

            [Theory]
            [InlineData(700.9f, 700)]
            [InlineData(1439.5f, 1439)]
            [InlineData(1440f, 1439)]
            [InlineData(-3f, 0)]
            public void Sends_the_world_minute_while_dreaming(
                float copy,
                int sent)
            {
                var clock = Due with { Dreaming = true, TimeCopy = copy };

                var minute = ClockRules.MinuteToSend(clock);

                minute.ShouldBe(sent);
            }
        }

        public class OwnClock
        {
            [Theory]
            [InlineData(true, true, false, ClockHold.Stop)]
            [InlineData(true, true, true, ClockHold.Stop)]
            [InlineData(false, true, true, ClockHold.Restart)]
            [InlineData(false, false, true, ClockHold.Forget)]
            [InlineData(true, false, true, ClockHold.Forget)]
            [InlineData(true, false, false, ClockHold.Leave)]
            [InlineData(false, true, false, ClockHold.Leave)]
            public void Holds_the_clock_while_a_client_copy(
                bool client,
                bool controller,
                bool stopped,
                ClockHold hold)
            {
                var result = ClockRules.OwnClock(client, controller, stopped);

                result.ShouldBe(hold);
            }
        }

        public class Takes
        {
            [Theory]
            [InlineData(true, true, true)]
            [InlineData(false, true, false)]
            [InlineData(true, false, false)]
            public void Takes_the_host_clock_as_a_client_copy(
                bool fromHost,
                bool client,
                bool takes)
            {
                var result = ClockRules.Takes(fromHost, client);

                result.ShouldBe(takes);
            }
        }

        public class CatchUp
        {
            [Fact]
            public void Steps_a_minute_behind()
            {
                var step = ClockRules.CatchUp(OneBehind);

                step.ShouldBe(new ClockStep
                {
                    Move = ClockMove.Step,
                    Minutes = 1,
                });
            }

            [Fact]
            public void Does_nothing_at_the_same_minute_and_day()
            {
                var clock = OneBehind with { HostMinute = 600 };

                var step = ClockRules.CatchUp(clock);

                step.Move.ShouldBe(ClockMove.Nothing);
            }

            [Fact]
            public void Ignores_it_with_no_controller()
            {
                var clock = OneBehind with { HasController = false };

                var step = ClockRules.CatchUp(clock);

                step.Move.ShouldBe(ClockMove.Ignore);
            }

            [Fact]
            public void Ignores_it_outside_a_world()
            {
                var clock = OneBehind with { InWorld = false };

                var step = ClockRules.CatchUp(clock);

                step.Move.ShouldBe(ClockMove.Ignore);
            }

            [Fact]
            public void Ignores_it_with_no_world_before_dreaming()
            {
                var clock = OneBehind with { InWorld = false, Dreaming = true };

                var step = ClockRules.CatchUp(clock);

                step.Move.ShouldBe(ClockMove.Ignore);
            }

            [Fact]
            public void Waits_while_dreaming()
            {
                var clock = OneBehind with { Dreaming = true };

                var step = ClockRules.CatchUp(clock);

                step.Move.ShouldBe(ClockMove.Wait);
            }

            [Fact]
            public void Steps_exactly_the_most_minutes()
            {
                var clock = OneBehind with { HostMinute = 720 };

                var step = ClockRules.CatchUp(clock);

                step.ShouldBe(new ClockStep
                {
                    Move = ClockMove.Step,
                    Minutes = 120,
                });
            }

            [Fact]
            public void Jumps_one_minute_past_the_most()
            {
                var clock = OneBehind with { HostMinute = 721 };

                var step = ClockRules.CatchUp(clock);

                step.Move.ShouldBe(ClockMove.Jump);
            }

            [Fact]
            public void Jumps_back_a_client_one_minute_ahead()
            {
                var clock = OneBehind with { HostMinute = 599 };

                var step = ClockRules.CatchUp(clock);

                step.Move.ShouldBe(ClockMove.Jump);
            }

            [Fact]
            public void Steps_no_minutes_with_the_day_off_by_one()
            {
                var clock = OneBehind with { HostMinute = 600, HostDay = 4 };

                var step = ClockRules.CatchUp(clock);

                step.ShouldBe(new ClockStep
                {
                    Move = ClockMove.Step,
                    Minutes = 0,
                });
            }

            [Fact]
            public void Steps_with_the_day_one_behind_ours()
            {
                var clock = OneBehind with { HostDay = 2 };

                var step = ClockRules.CatchUp(clock);

                step.Move.ShouldBe(ClockMove.Step);
            }

            [Fact]
            public void Jumps_with_the_day_off_by_two()
            {
                var clock = OneBehind with { HostDay = 5 };

                var step = ClockRules.CatchUp(clock);

                step.Move.ShouldBe(ClockMove.Jump);
            }

            [Fact]
            public void Steps_round_midnight()
            {
                var clock = OneBehind with { Minute = 1430, HostMinute = 10 };

                var step = ClockRules.CatchUp(clock);

                step.Minutes.ShouldBe(20);
            }
        }

        public class Behind
        {
            [Theory]
            [InlineData(601, 600, 1)]
            [InlineData(600, 600, 0)]
            [InlineData(599, 600, 1439)]
            [InlineData(10, 1430, 20)]
            [InlineData(0, 1439, 1)]
            [InlineData(1500, 0, 60)]
            public void Counts_minutes_round_the_clock(
                int host,
                int ours,
                int behind)
            {
                var result = ClockRules.Behind(host, ours);

                result.ShouldBe(behind);
            }
        }
    }
}
