using DarkwoodMP.Combat.Motion;

namespace DarkwoodMP.Tests.Combat.Motion
{
    public class MotionTrackTests
    {
        static readonly Facing North = new Facing(0f, 0f, 0f, 1f);

        static readonly Facing East = new Facing(0f, 0.7071f, 0f, 0.7071f);

        static Seconds S(float value) => new Seconds(value);

        static Vec3 X(float x) => new Vec3(x, 0f, 0f);

        /// An enemy sent at 0 s at the origin, then at 0.125 s 10 east,
        /// turned: moving at 80 a second.
        static MotionTrack Walking()
        {
            var track = new MotionTrack();
            track.Add(S(0f), X(0f), North);
            track.Add(S(0.125f), X(10f), East);
            return track;
        }

        public class Limits
        {
            [Fact]
            public void Pins_the_track_limits()
            {
                var limits = new[]
                {
                    MotionTrack.MaxSamples,
                    MotionTrack.Gap,
                    MotionTrack.Teleport,
                    MotionTrack.MaxAhead,
                    MotionTrack.MinSpan,
                    MotionTrack.MaxCarrySpan,
                };

                limits.ShouldBe(new[]
                {
                    12f, 0.75f, 600f, 0.15f, 0.001f, 0.3f,
                });
            }
        }

        public class Add
        {
            [Fact]
            public void Keeps_at_most_twelve_states()
            {
                var track = new MotionTrack();

                for (int i = 0; i < 13; i++)
                {
                    track.Add(S(i * 0.0625f), X(i), North);
                }

                track.Samples.Count.ShouldBe(12);
                track.Samples[0].Pos.X.ShouldBe(1f);
            }

            [Fact]
            public void Keeps_the_track_for_a_state_exactly_the_gap_later()
            {
                var track = Walking();

                track.Add(S(0.875f), X(20f), North);

                track.Samples.Count.ShouldBe(3);
            }

            [Fact]
            public void Starts_afresh_a_state_just_past_the_gap()
            {
                var track = Walking();

                track.Add(S(0.876f), X(20f), North);

                track.Samples.Count.ShouldBe(1);
            }

            [Fact]
            public void Keeps_the_track_for_a_move_exactly_the_teleport()
            {
                var track = Walking();

                track.Add(S(0.25f), X(610f), North);

                track.Samples.Count.ShouldBe(3);
            }

            [Fact]
            public void Starts_afresh_a_move_just_past_the_teleport()
            {
                var track = Walking();

                track.Add(S(0.25f), X(610.5f), North);

                track.Samples.Count.ShouldBe(1);
            }

            [Fact]
            public void Measures_the_teleport_in_three_dimensions()
            {
                var track = Walking();

                track.Add(S(0.25f), new Vec3(10f, 600.5f, 0f), North);

                track.Samples.Count.ShouldBe(1);
            }

            [Fact]
            public void Replaces_the_last_state_sent_at_the_same_moment()
            {
                var track = Walking();

                track.Add(S(0.125f), X(12f), North);

                track.Samples.Count.ShouldBe(2);
                track.Samples[1].Pos.X.ShouldBe(12f);
                track.Samples[1].Rot.ShouldBe(North);
            }

            [Fact]
            public void Keeps_the_last_time_for_a_state_sent_earlier()
            {
                var track = Walking();

                track.Add(S(0.0625f), X(12f), North);

                track.Samples.Count.ShouldBe(2);
                track.Samples[1].Sent.Value.ShouldBe(0.125f);
                track.Samples[1].Pos.X.ShouldBe(12f);
            }
        }

        public class Where
        {
            [Fact]
            public void Says_false_with_no_state()
            {
                var track = new MotionTrack();

                bool drawn = track.Where(S(1f), out var pose);

                drawn.ShouldBeFalse();
                pose.From.ShouldBe(Facing.Identity);
            }

            [Fact]
            public void Draws_a_single_state_where_it_is()
            {
                var track = new MotionTrack();
                track.Add(S(0f), X(5f), East);

                track.Where(S(1f), out var pose);

                pose.Pos.X.ShouldBe(5f);
                pose.From.ShouldBe(East);
                pose.Blends.ShouldBeFalse();
                track.Speed.ShouldBe(0f);
            }

            [Fact]
            public void Draws_a_moment_before_the_first_state_at_it()
            {
                var track = Walking();

                track.Where(S(-1f), out var pose);

                pose.Pos.X.ShouldBe(0f);
                pose.From.ShouldBe(North);
                track.Speed.ShouldBe(0f);
            }

            [Fact]
            public void Blends_between_the_two_states_around_the_moment()
            {
                var track = Walking();

                track.Where(S(0.0625f), out var pose);

                pose.Pos.X.ShouldBe(5f);
                pose.Blends.ShouldBeTrue();
                pose.From.ShouldBe(North);
                pose.To.ShouldBe(East);
                pose.Blend.ShouldBe(0.5f);
                track.Speed.ShouldBe(80f);
            }

            [Fact]
            public void Measures_the_speed_on_the_ground_only()
            {
                var track = new MotionTrack();
                track.Add(S(0f), new Vec3(0f, 0f, 0f), North);
                track.Add(S(0.125f), new Vec3(0f, 50f, 10f), North);

                track.Where(S(0.0625f), out _);

                track.Speed.ShouldBe(80f);
            }

            [Fact]
            public void Drops_the_states_older_than_the_one_before()
            {
                var track = Walking();
                track.Add(S(0.25f), X(20f), North);

                track.Where(S(0.1875f), out var pose);

                track.Samples.Count.ShouldBe(2);
                pose.Pos.X.ShouldBe(15f);
            }

            [Fact]
            public void Carries_on_past_the_last_state_at_its_speed()
            {
                var track = Walking();

                track.Where(S(0.1875f), out var pose);

                pose.Pos.X.ShouldBe(15f);
                pose.From.ShouldBe(East);
                pose.Blends.ShouldBeFalse();
                track.Speed.ShouldBe(80f);
            }

            [Fact]
            public void Carries_on_for_at_most_the_most_ahead()
            {
                var track = Walking();

                track.Where(S(1f), out var pose);

                pose.Pos.X.ShouldBe(22f, 0.001f);
                track.Speed.ShouldBe(0f);
            }

            [Fact]
            public void Keeps_the_speed_just_inside_the_most_ahead()
            {
                var track = Walking();

                track.Where(S(0.125f + 0.149f), out _);

                track.Speed.ShouldBe(80f, 0.001f);
            }

            [Fact]
            public void Drops_the_speed_just_past_the_most_ahead()
            {
                var track = Walking();

                track.Where(S(0.125f + 0.151f), out _);

                track.Speed.ShouldBe(0f);
            }

            [Fact]
            public void Holds_at_the_last_state_after_a_span_of_the_longest()
            {
                var track = new MotionTrack();
                track.Add(S(0f), X(0f), North);
                track.Add(S(0.3f), X(30f), North);

                track.Where(S(0.4f), out var pose);

                pose.Pos.X.ShouldBe(30f);
                track.Speed.ShouldBe(0f);
            }

            [Fact]
            public void Carries_on_after_a_span_just_inside_the_longest()
            {
                var track = new MotionTrack();
                track.Add(S(0f), X(0f), North);
                track.Add(S(0.299f), X(30f), North);

                track.Where(S(0.399f), out var pose);

                pose.Pos.X.ShouldBeGreaterThan(30f);
            }

            [Fact]
            public void Holds_at_the_last_state_after_a_span_of_the_shortest()
            {
                var track = new MotionTrack();
                track.Add(S(0f), X(0f), North);
                track.Add(S(0.001f), X(1f), North);

                track.Where(S(0.05f), out var pose);

                pose.Pos.X.ShouldBe(1f);
                track.Speed.ShouldBe(0f);
            }
        }
    }
}
