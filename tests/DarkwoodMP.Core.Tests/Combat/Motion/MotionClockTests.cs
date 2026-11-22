using DarkwoodMP.Combat.Motion;

namespace DarkwoodMP.Tests.Combat.Motion
{
    public class MotionClockTests
    {
        static Seconds S(float value) => new Seconds(value);

        /// A clock whose first batch came 0.5 s after it was sent.
        static MotionClock Started()
        {
            var clock = new MotionClock();
            clock.Arrived(S(10f), S(10.5f));
            return clock;
        }

        public class Limits
        {
            [Fact]
            public void Pins_the_clock_limits()
            {
                var limits = new[]
                {
                    MotionClock.MaxDelay,
                    MotionClock.StateInterval,
                    MotionClock.Resync,
                    MotionClock.OffsetRise,
                    MotionClock.JitterRate,
                    MotionClock.IntervalRate,
                    MotionClock.LongestStep,
                    MotionClock.DelayRate,
                };

                limits.ShouldBe(new[]
                {
                    0.03f, 0.05f, 2f, 0.01f, 0.05f, 0.1f, 0.5f, 0.05f,
                });
            }
        }

        public class Arrived
        {
            [Fact]
            public void Takes_the_first_gap_as_the_offset()
            {
                var clock = Started();

                clock.Offset.Value.ShouldBe(0.5f);
            }

            [Fact]
            public void Drops_at_once_to_a_quicker_gap()
            {
                var clock = Started();

                clock.Arrived(S(11f), S(11.25f));

                clock.Offset.Value.ShouldBe(0.25f);
            }

            [Fact]
            public void Rises_by_a_hundredth_of_a_slower_gap()
            {
                var clock = Started();

                clock.Arrived(S(11f), S(12.5f));

                clock.Offset.Value.ShouldBe(0.51f, 0.00001f);
            }

            [Fact]
            public void Follows_a_gap_exactly_the_resync_away()
            {
                var clock = Started();

                clock.Arrived(S(11f), S(13.5f));

                clock.Offset.Value.ShouldBe(0.52f, 0.00001f);
            }

            [Fact]
            public void Takes_a_gap_just_past_the_resync_as_a_new_clock()
            {
                var clock = Started();

                clock.Arrived(S(11f), S(13.5625f));

                clock.Offset.Value.ShouldBe(2.5625f);
                clock.Jitter.Value.ShouldBe(0f);
            }

            [Fact]
            public void Follows_how_far_a_gap_is_off_by_a_twentieth()
            {
                var clock = Started();

                clock.Arrived(S(11f), S(12.5f));

                clock.Jitter.Value.ShouldBe(0.99f * 0.05f, 0.00001f);
            }

            [Fact]
            public void Follows_the_step_between_batches_by_a_tenth()
            {
                var clock = Started();

                clock.Arrived(S(10.25f), S(10.75f));

                clock.Interval.Value.ShouldBe(0.07f, 0.00001f);
            }

            [Fact]
            public void Keeps_the_interval_for_a_step_of_the_longest()
            {
                var clock = Started();

                clock.Arrived(S(10.5f), S(11f));

                clock.Interval.Value.ShouldBe(0.05f);
            }

            [Fact]
            public void Keeps_the_interval_for_a_batch_sent_no_later()
            {
                var clock = Started();

                clock.Arrived(S(10f), S(10.75f));

                clock.Interval.Value.ShouldBe(0.05f);
            }
        }

        public class Frame
        {
            /// A clock whose arrivals vary by 0.0495 s.
            static MotionClock Varying()
            {
                var clock = Started();
                clock.Arrived(S(11f), S(12.5f));
                return clock;
            }

            [Fact]
            public void Moves_the_delay_by_a_twentieth_of_the_frame()
            {
                var clock = Varying();

                clock.Frame(S(0.1f));

                clock.Delay.Value.ShouldBe(0.005f, 0.00001f);
            }

            [Fact]
            public void Reaches_half_the_jitter()
            {
                var clock = Varying();

                clock.Frame(S(1f));

                clock.Delay.Value.ShouldBe(clock.Jitter.Value * 0.5f);
            }

            [Fact]
            public void Holds_the_delay_to_the_most()
            {
                var clock = Started();
                clock.Arrived(S(11f), S(13.4f));

                clock.Frame(S(10f));

                clock.Delay.Value.ShouldBe(0.03f);
            }

            [Fact]
            public void Keeps_no_delay_when_arrivals_never_vary()
            {
                var clock = Started();

                clock.Frame(S(1f));

                clock.Delay.Value.ShouldBe(0f);
            }
        }

        public class DrawnAt
        {
            [Fact]
            public void Draws_at_our_time_less_the_offset_and_the_delay()
            {
                var clock = Started();
                clock.Arrived(S(11f), S(12.5f));
                clock.Frame(S(0.1f));

                var at = clock.DrawnAt(S(20f));

                at.Value.ShouldBe(20f - 0.51f - 0.005f, 0.00001f);
            }
        }

        public class Reset
        {
            [Fact]
            public void Takes_the_next_gap_as_a_new_clock()
            {
                var clock = Started();
                clock.Arrived(S(11f), S(12.5f));
                clock.Frame(S(1f));

                clock.Reset();
                clock.Arrived(S(20f), S(21f));

                clock.Offset.Value.ShouldBe(1f);
                clock.Jitter.Value.ShouldBe(0f);
                clock.Delay.Value.ShouldBe(0f);
                clock.Interval.Value.ShouldBe(0.05f);
            }
        }
    }
}
