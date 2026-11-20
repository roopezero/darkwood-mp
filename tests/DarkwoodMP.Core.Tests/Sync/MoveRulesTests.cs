using DarkwoodMP.Sync;

namespace DarkwoodMP.Tests.Sync
{
    public class MoveRulesTests
    {
        static readonly Vec3 Start = new Vec3(0f, 7f, 0f);

        /// A player the host has tracked since time 0, standing at Start,
        /// never put back, reporting the same spot half a second later.
        static readonly MoveFacts Tracked = new MoveFacts
        {
            Known = true,
            Was = new PlayerMovement
            {
                Accepted = Start,
                MeasureFrom = Start.Ground,
                MeasureAt = new Seconds(0f),
                PutBackAt = new Seconds(-10f),
            },
            Reported = Start,
            Now = new Seconds(0.5f),
        };

        static MoveFacts To(float x) =>
            Tracked with { Reported = new Vec3(x, 0f, 0f) };

        public class Limits
        {
            [Fact]
            public void Pins_the_movement_limits()
            {
                var limits = new[]
                {
                    MoveRules.MaxSpeed,
                    MoveRules.Teleport,
                    MoveRules.TripLanding,
                    MoveRules.Window,
                    MoveRules.NeverPutBack,
                };

                var pinned = new[] { 2000f, 3000f, 400f, 0.5f, -10f };

                limits.ShouldBe(pinned);
            }
        }

        public class AsksForTrip
        {
            [Theory]
            [InlineData(400f, false)]
            [InlineData(401f, true)]
            [InlineData(3000f, true)]
            [InlineData(3001f, false)]
            public void Asks_only_between_trip_landing_and_teleport(
                float x,
                bool asks)
            {
                var move = To(x);

                var asked = MoveRules.AsksForTrip(move);

                asked.ShouldBe(asks);
            }

            [Fact]
            public void Does_not_ask_about_an_unknown_player()
            {
                var move = To(1000f) with { Known = false };

                var asked = MoveRules.AsksForTrip(move);

                asked.ShouldBeFalse();
            }
        }

        public class Check
        {
            [Fact]
            public void Starts_a_track_for_an_unknown_player()
            {
                var move = To(2000f) with { Known = false, Was = default };

                var step = MoveRules.Check(move);

                step.Outcome.ShouldBe(MoveOutcome.Started);
                step.Accepted.ShouldBe(move.Reported);
                step.Next.MeasureFrom.ShouldBe(move.Reported.Ground);
                step.Next.MeasureAt.ShouldBe(move.Now);
                step.Next.PutBackAt.Value.ShouldBe(MoveRules.NeverPutBack);
            }

            [Fact]
            public void Starts_again_after_a_jump_past_teleport()
            {
                var move = To(3001f);

                var step = MoveRules.Check(move);

                step.Outcome.ShouldBe(MoveOutcome.Started);
                step.SendCorrection.ShouldBeFalse();
            }

            [Fact]
            public void Measures_a_jump_of_exactly_teleport()
            {
                var move = To(3000f);

                var step = MoveRules.Check(move);

                step.Outcome.ShouldBe(MoveOutcome.PutBack);
            }

            [Fact]
            public void Judges_a_teleport_from_the_accepted_spot()
            {
                var from = new Vec2(-1000f, 0f);
                var was = Tracked.Was with { MeasureFrom = from };
                var move = To(2500f) with { Was = was };

                var step = MoveRules.Check(move);

                step.Outcome.ShouldBe(MoveOutcome.PutBack);
            }

            [Fact]
            public void Measures_the_speed_from_where_the_window_began()
            {
                var from = new Vec2(-500f, 0f);
                var was = Tracked.Was with { MeasureFrom = from };
                var move = To(600f) with { Was = was };

                var step = MoveRules.Check(move);

                step.Outcome.ShouldBe(MoveOutcome.PutBack);
            }

            [Fact]
            public void Starts_again_where_an_agreed_trip_lands()
            {
                var move = To(401f) with { TripLanded = true };

                var step = MoveRules.Check(move);

                step.Outcome.ShouldBe(MoveOutcome.Tripped);
                step.Next.MeasureFrom.ShouldBe(move.Reported.Ground);
            }

            [Fact]
            public void Ignores_a_trip_within_trip_landing()
            {
                var move = To(400f) with { TripLanded = true };

                var step = MoveRules.Check(move);

                step.Outcome.ShouldBe(MoveOutcome.Accepted);
            }

            [Fact]
            public void Takes_a_teleport_over_a_trip()
            {
                var move = To(3001f) with { TripLanded = true };

                var step = MoveRules.Check(move);

                step.Outcome.ShouldBe(MoveOutcome.Started);
            }

            [Fact]
            public void Accepts_exactly_the_top_speed()
            {
                var move = To(1000f);

                var step = MoveRules.Check(move);

                step.Outcome.ShouldBe(MoveOutcome.Accepted);
                step.Accepted.ShouldBe(move.Reported);
            }

            [Fact]
            public void Puts_back_a_player_just_past_the_top_speed()
            {
                var move = To(1000.5f);

                var step = MoveRules.Check(move);

                step.Outcome.ShouldBe(MoveOutcome.PutBack);
                step.Speed.ShouldBe(2001f, 0.01f);
            }

            [Fact]
            public void Measures_over_at_least_the_window()
            {
                var move = To(1000f) with { Now = new Seconds(0.1f) };

                var step = MoveRules.Check(move);

                step.Outcome.ShouldBe(MoveOutcome.Accepted);
            }

            [Fact]
            public void Accepts_the_top_speed_over_a_longer_window()
            {
                var move = To(2000f) with { Now = new Seconds(1f) };

                var step = MoveRules.Check(move);

                step.Outcome.ShouldBe(MoveOutcome.Accepted);
            }

            [Fact]
            public void Puts_back_just_past_the_top_speed_over_a_longer_window()
            {
                var move = To(2001f) with { Now = new Seconds(1f) };

                var step = MoveRules.Check(move);

                step.Outcome.ShouldBe(MoveOutcome.PutBack);
            }

            [Fact]
            public void Keeps_the_accepted_position_with_its_height()
            {
                var move = To(1100f);

                var step = MoveRules.Check(move);

                step.Accepted.ShouldBe(Start);
                step.Next.Accepted.ShouldBe(Start);
            }

            [Fact]
            public void Corrects_a_player_never_put_back()
            {
                var move = To(1100f);

                var step = MoveRules.Check(move);

                step.SendCorrection.ShouldBeTrue();
                step.Next.PutBackAt.ShouldBe(move.Now);
            }

            [Fact]
            public void Holds_back_without_a_correction_exactly_a_window_on()
            {
                var was = Tracked.Was with { PutBackAt = new Seconds(0.25f) };
                var move = To(2000f) with
                {
                    Was = was,
                    Now = new Seconds(0.75f),
                };

                var step = MoveRules.Check(move);

                step.Outcome.ShouldBe(MoveOutcome.PutBack);
                step.SendCorrection.ShouldBeFalse();
                step.Next.PutBackAt.ShouldBe(was.PutBackAt);
            }

            [Fact]
            public void Corrects_again_just_past_a_window_on()
            {
                var was = Tracked.Was with { PutBackAt = new Seconds(0.24f) };
                var move = To(2000f) with
                {
                    Was = was,
                    Now = new Seconds(0.75f),
                };

                var step = MoveRules.Check(move);

                step.SendCorrection.ShouldBeTrue();
            }

            [Fact]
            public void Measures_again_from_where_it_kept_them()
            {
                var from = new Vec2(-500f, 0f);
                var was = Tracked.Was with { MeasureFrom = from };
                var move = To(1100f) with { Was = was };

                var step = MoveRules.Check(move);

                step.Next.MeasureFrom.ShouldBe(Start.Ground);
                step.Next.MeasureAt.ShouldBe(move.Now);
            }

            [Fact]
            public void Restarts_the_window_after_a_whole_window()
            {
                var move = To(100f);

                var step = MoveRules.Check(move);

                step.Next.MeasureFrom.ShouldBe(move.Reported.Ground);
                step.Next.MeasureAt.ShouldBe(move.Now);
            }

            [Fact]
            public void Keeps_the_window_within_a_window()
            {
                var move = To(100f) with { Now = new Seconds(0.25f) };

                var step = MoveRules.Check(move);

                step.Next.Accepted.ShouldBe(move.Reported);
                step.Next.MeasureFrom.ShouldBe(Tracked.Was.MeasureFrom);
                step.Next.MeasureAt.ShouldBe(Tracked.Was.MeasureAt);
            }

            [Fact]
            public void Keeps_when_it_last_corrected_on_accepting()
            {
                var was = Tracked.Was with { PutBackAt = new Seconds(0.3f) };
                var move = To(100f) with { Was = was };

                var step = MoveRules.Check(move);

                step.Next.PutBackAt.ShouldBe(was.PutBackAt);
            }
        }
    }
}
