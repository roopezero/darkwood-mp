using DarkwoodMP.Sync;

namespace DarkwoodMP.Tests.Sync
{
    public class MoveTrackerTests
    {
        const ulong Player = 7;

        static Vec3 At(float x) => new Vec3(x, 0f, 0f);

        static Seconds T(float s) => new Seconds(s);

        /// A tracker that has seen the player at x 0, at time 0, and has
        /// agreed to a trip to x 1000 until time 10.
        static MoveTracker WithTrip()
        {
            var moves = new MoveTracker();
            moves.Check(Player, At(0f), T(0f));
            moves.ExpectTrip(Player, new Vec2(1000f, 0f), T(10f));
            return moves;
        }

        public class Check
        {
            [Fact]
            public void Takes_the_first_position_as_it_is()
            {
                var moves = new MoveTracker();

                var step = moves.Check(Player, At(2000f), T(0f));

                step.Outcome.ShouldBe(MoveOutcome.Started);
            }

            [Fact]
            public void Lets_a_player_land_where_an_agreed_trip_goes()
            {
                var moves = WithTrip();

                var step = moves.Check(Player, At(1399f), T(0.1f));

                step.Outcome.ShouldBe(MoveOutcome.Tripped);
            }

            [Fact]
            public void Puts_back_a_jump_to_trip_landing_from_the_trip()
            {
                var moves = WithTrip();

                var step = moves.Check(Player, At(1400f), T(0.1f));

                step.Outcome.ShouldBe(MoveOutcome.PutBack);
            }

            [Fact]
            public void Uses_up_a_trip_once()
            {
                var moves = WithTrip();
                moves.Check(Player, At(1000f), T(0.1f));
                moves.Check(Player, At(0f), T(1f));

                var step = moves.Check(Player, At(1100f), T(1.1f));

                step.Outcome.ShouldBe(MoveOutcome.PutBack);
            }

            [Fact]
            public void Uses_up_the_first_trip_that_lands_there()
            {
                var moves = WithTrip();
                moves.ExpectTrip(Player, new Vec2(1100f, 0f), T(1f));
                moves.Check(Player, At(1050f), T(0.1f));
                moves.Forget(Player);
                moves.Check(Player, At(0f), T(2f));

                var step = moves.Check(Player, At(1050f), T(2.1f));

                step.Outcome.ShouldBe(MoveOutcome.PutBack);
            }

            [Fact]
            public void Uses_up_only_the_trip_that_lands_there()
            {
                var moves = WithTrip();
                moves.ExpectTrip(Player, new Vec2(-1000f, 0f), T(10f));
                moves.Check(Player, At(-1000f), T(0.1f));
                moves.Forget(Player);
                moves.Check(Player, At(0f), T(1f));

                var step = moves.Check(Player, At(1050f), T(1.1f));

                step.Outcome.ShouldBe(MoveOutcome.Tripped);
            }

            [Fact]
            public void Remembers_when_it_put_the_player_back()
            {
                var moves = new MoveTracker();
                moves.Check(Player, At(0f), T(0f));
                moves.Check(Player, At(1100f), T(0.1f));

                var step = moves.Check(Player, At(1100f), T(0.2f));

                step.Outcome.ShouldBe(MoveOutcome.PutBack);
                step.SendCorrection.ShouldBeFalse();
            }

            [Fact]
            public void Keeps_a_trip_live_at_its_end()
            {
                var moves = WithTrip();

                var step = moves.Check(Player, At(1000f), T(10f));

                step.Outcome.ShouldBe(MoveOutcome.Tripped);
            }

            [Fact]
            public void Drops_a_trip_past_its_end()
            {
                var moves = WithTrip();

                var step = moves.Check(Player, At(1000f), T(10.5f));

                step.Outcome.ShouldBe(MoveOutcome.Accepted);
            }

            [Fact]
            public void Drops_every_trip_past_its_end()
            {
                var moves = WithTrip();
                moves.ExpectTrip(Player, new Vec2(-1000f, 0f), T(1f));
                moves.ExpectTrip(Player, new Vec2(-1500f, 0f), T(1f));

                var step = moves.Check(Player, At(-1500f), T(2f));

                step.Outcome.ShouldBe(MoveOutcome.Accepted);
            }

            [Fact]
            public void Leaves_a_trip_to_a_move_within_trip_landing()
            {
                var moves = new MoveTracker();
                moves.Check(Player, At(0f), T(0f));
                moves.ExpectTrip(Player, new Vec2(100f, 0f), T(10f));
                moves.Check(Player, At(100f), T(0.5f));
                moves.Check(Player, At(500f), T(1f));

                var step = moves.Check(Player, At(-200f), T(1.1f));

                step.Outcome.ShouldBe(MoveOutcome.Tripped);
            }
        }

        public class ExpectTrip
        {
            [Fact]
            public void Keeps_each_trip_it_agreed_to()
            {
                var moves = WithTrip();
                moves.ExpectTrip(Player, new Vec2(-1000f, 0f), T(10f));

                var step = moves.Check(Player, At(1000f), T(0.1f));

                step.Outcome.ShouldBe(MoveOutcome.Tripped);
            }
        }

        public class TryAccepted
        {
            [Fact]
            public void Gives_where_the_check_let_the_player_be()
            {
                var moves = new MoveTracker();
                moves.Check(Player, At(250f), T(0f));
                moves.Check(Player, At(2500f), T(0.1f));

                var known = moves.TryAccepted(Player, out var pos);

                known.ShouldBeTrue();
                pos.ShouldBe(At(250f));
            }

            [Fact]
            public void Knows_nothing_of_an_unseen_player()
            {
                var moves = new MoveTracker();

                var known = moves.TryAccepted(Player, out _);

                known.ShouldBeFalse();
            }
        }

        public class Forget
        {
            [Fact]
            public void Keeps_the_trips()
            {
                var moves = WithTrip();
                moves.Forget(Player);
                moves.Check(Player, At(0f), T(1f));

                var step = moves.Check(Player, At(1000f), T(1.1f));

                step.Outcome.ShouldBe(MoveOutcome.Tripped);
            }

            [Fact]
            public void Takes_the_next_position_as_it_is()
            {
                var moves = new MoveTracker();
                moves.Check(Player, At(0f), T(0f));
                moves.Forget(Player);

                var step = moves.Check(Player, At(2000f), T(0.1f));

                step.Outcome.ShouldBe(MoveOutcome.Started);
            }
        }

        public class Leave
        {
            [Fact]
            public void Drops_the_trips()
            {
                var moves = WithTrip();
                moves.Leave(Player);
                moves.Check(Player, At(0f), T(1f));

                var step = moves.Check(Player, At(1100f), T(1.1f));

                step.Outcome.ShouldBe(MoveOutcome.PutBack);
            }

            [Fact]
            public void Drops_the_track()
            {
                var moves = WithTrip();
                moves.Leave(Player);

                var known = moves.TryAccepted(Player, out _);

                known.ShouldBeFalse();
            }
        }

        public class Clear
        {
            [Fact]
            public void Drops_every_track()
            {
                var moves = WithTrip();
                moves.Clear();

                var known = moves.TryAccepted(Player, out _);

                known.ShouldBeFalse();
            }

            [Fact]
            public void Drops_every_trip()
            {
                var moves = WithTrip();
                moves.Clear();
                moves.Check(Player, At(0f), T(1f));

                var step = moves.Check(Player, At(1100f), T(1.1f));

                step.Outcome.ShouldBe(MoveOutcome.PutBack);
            }
        }
    }
}
