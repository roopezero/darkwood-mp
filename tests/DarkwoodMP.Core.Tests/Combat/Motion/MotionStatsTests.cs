using DarkwoodMP.Combat.Motion;

namespace DarkwoodMP.Tests.Combat.Motion
{
    public class MotionStatsTests
    {
        static Seconds S(float value) => new Seconds(value);

        static Vec3 X(float x) => new Vec3(x, 0f, 0f);

        /// A track the host has moving at speed a second, drawn once at
        /// the origin.
        static MotionTrack Moving(MotionStats stats, float speed)
        {
            var track = new MotionTrack();
            track.Add(S(0f), X(0f), Facing.Identity);
            track.Add(S(0.25f), X(speed * 0.25f), Facing.Identity);
            track.Where(S(0.125f), out _);
            stats.Drawn(track, X(0f), S(0.1f));
            return track;
        }

        static MotionStats Counting() => new MotionStats { On = true };

        public class Limits
        {
            [Fact]
            public void Pins_the_measuring_limits()
            {
                var limits = new[]
                {
                    MotionStats.MinSpeed,
                    MotionStats.MaxSpeed,
                    MotionStats.StallShare,
                    MotionStats.JumpShare,
                    MotionStats.MaxError,
                    MotionStats.LateGap,
                    MotionStats.MinFrame,
                };

                limits.ShouldBe(new[]
                {
                    40f, 2000f, 0.25f, 2.5f, 3f, 0.15f, 0.0001f,
                });
            }
        }

        public class Drawn
        {
            [Fact]
            public void Counts_nothing_while_off()
            {
                var stats = new MotionStats();
                var track = Moving(stats, 100f);

                stats.Drawn(track, X(10f), S(0.1f));

                stats.Moving.ShouldBe(0);
                track.HasDrawn.ShouldBeFalse();
            }

            [Fact]
            public void Measures_nothing_the_first_time_drawn()
            {
                var stats = Counting();

                var track = Moving(stats, 100f);

                stats.Moving.ShouldBe(0);
                track.HasDrawn.ShouldBeTrue();
            }

            [Fact]
            public void Counts_a_move_of_the_expected_length_as_smooth()
            {
                var stats = Counting();
                var track = Moving(stats, 100f);

                stats.Drawn(track, X(10f), S(0.1f));

                stats.Moving.ShouldBe(1);
                stats.Stalls.ShouldBe(0);
                stats.Jumps.ShouldBe(0);
                stats.SumErr.ShouldBe(0f, 0.0001f);
            }

            [Theory]
            [InlineData(2.4f, 1, 0)]
            [InlineData(2.6f, 0, 0)]
            [InlineData(24.9f, 0, 0)]
            [InlineData(25.1f, 0, 1)]
            public void Tells_a_stall_and_a_jump_by_their_share(
                float moved,
                int stalls,
                int jumps)
            {
                var stats = Counting();
                var track = Moving(stats, 100f);

                stats.Drawn(track, X(moved), S(0.1f));

                stats.Stalls.ShouldBe(stalls);
                stats.Jumps.ShouldBe(jumps);
            }

            [Theory]
            [InlineData(39.9f, 0)]
            [InlineData(40f, 1)]
            [InlineData(1999.9f, 1)]
            [InlineData(2000f, 0)]
            public void Measures_only_the_speeds_it_counts_as_moving(
                float speed,
                int moving)
            {
                var stats = Counting();
                var track = Moving(stats, speed);

                stats.Drawn(track, X(1f), S(0.1f));

                stats.Moving.ShouldBe(moving);
            }

            [Fact]
            public void Holds_one_frame_s_error_to_the_most()
            {
                var stats = Counting();
                var track = Moving(stats, 100f);

                stats.Drawn(track, X(100f), S(0.1f));

                stats.SumErr.ShouldBe(3f);
            }

            [Fact]
            public void Skips_a_frame_of_the_shortest()
            {
                var stats = Counting();
                var track = Moving(stats, 100f);

                stats.Drawn(track, X(10f), S(0.0001f));

                stats.Moving.ShouldBe(0);
                track.Drawn.X.ShouldBe(10f);
            }
        }

        public class Batch
        {
            [Fact]
            public void Counts_a_gap_over_the_late_one_as_late()
            {
                var clock = new MotionClock();
                clock.Stats.On = true;

                clock.Arrived(S(1f), S(1f));
                clock.Arrived(S(1.0625f), S(1.15f));
                clock.Arrived(S(1.125f), S(1.3125f));

                clock.Stats.Batches.ShouldBe(3);
                clock.Stats.LateGaps.ShouldBe(1);
                clock.Stats.MaxGap.ShouldBe(0.1625f, 0.00001f);
                clock.Stats.MaxHostGap.ShouldBe(0.0625f);
            }

            [Fact]
            public void Counts_nothing_while_off()
            {
                var clock = new MotionClock();

                clock.Arrived(S(1f), S(1f));
                clock.Frame(S(0.5f));

                clock.Stats.Batches.ShouldBe(0);
                clock.Stats.Frames.ShouldBe(0);
            }
        }

        public class Summary
        {
            [Fact]
            public void Says_a_dash_for_the_shares_with_nothing_moving()
            {
                var clock = new MotionClock();

                var text = clock.Stats.Summary(clock);

                text.ShouldBe(
                    "0 batches, gap avg 0 ms p95 0 max 0 (over 150 ms: 0; "
                    + "host's max 0); 0 frames, slowest 0 ms; moving enemy "
                    + "frames 0: stalls 0 (-), jumps 0 (-), speed error avg "
                    + "0%; delay 0 ms (interval 50, jitter 0)");
            }

            [Fact]
            public void Gives_the_shares_to_a_tenth_of_a_percent()
            {
                var clock = new MotionClock();
                clock.Stats.On = true;
                var track = Moving(clock.Stats, 100f);
                clock.Stats.Drawn(track, X(2f), S(0.1f));

                var text = clock.Stats.Summary(clock);

                text.ShouldContain("stalls 1 (100.0%), jumps 0 (0.0%)");
            }
        }
    }
}
