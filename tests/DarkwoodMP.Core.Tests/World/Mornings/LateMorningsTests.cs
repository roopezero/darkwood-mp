using DarkwoodMP.World.Mornings;

namespace DarkwoodMP.Tests.World.Mornings
{
    public class LateMorningsTests
    {
        /// Day 3, in a world with player sync.
        static readonly LateFacts DayThree = new LateFacts
        {
            HasSync = true,
            InWorld = true,
            Today = 3,
        };

        /// A connected joiner still in a morning.
        static readonly RemoteMorning StillIn = new RemoteMorning
        {
            HasState = true,
            InMorning = true,
            IsPeer = true,
        };

        static LateMornings OverOnDayThree()
        {
            var late = new LateMornings();
            late.Over(3);
            return late;
        }

        public class Check
        {
            [Fact]
            public void Looks_at_the_joiners_on_the_day_it_is_over()
            {
                OverOnDayThree().Check(DayThree).ShouldBeTrue();
            }

            [Fact]
            public void Looks_at_nobody_with_no_morning_over()
            {
                new LateMornings().Check(DayThree).ShouldBeFalse();
            }

            [Fact]
            public void Forgets_the_morning_on_a_new_day()
            {
                var late = OverOnDayThree();

                late.Check(DayThree with { Today = 4 }).ShouldBeFalse();

                late.OverDay.ShouldBe(LateMornings.None);
            }

            [Fact]
            public void Keeps_the_morning_out_of_a_world()
            {
                var late = OverOnDayThree();

                late.Check(DayThree with { InWorld = false }).ShouldBeFalse();

                late.OverDay.ShouldBe(3);
            }

            [Fact]
            public void Looks_at_nobody_without_player_sync()
            {
                var late = OverOnDayThree();

                late.Check(DayThree with { HasSync = false }).ShouldBeFalse();
            }
        }

        public class Tell
        {
            [Fact]
            public void Tells_a_joiner_still_in_a_morning()
            {
                var late = OverOnDayThree();

                late.Tell(7, StillIn, new Seconds(10f)).ShouldBeTrue();
            }

            [Fact]
            public void Waits_just_before_the_next_telling()
            {
                var late = OverOnDayThree();
                late.Tell(7, StillIn, new Seconds(10f));

                late.Tell(7, StillIn, new Seconds(12.9f)).ShouldBeFalse();
            }

            [Fact]
            public void Tells_again_exactly_at_the_next_telling()
            {
                var late = OverOnDayThree();
                late.Tell(7, StillIn, new Seconds(10f));

                late.Tell(7, StillIn, new Seconds(13f)).ShouldBeTrue();
            }

            [Fact]
            public void Times_each_joiner_apart()
            {
                var late = OverOnDayThree();
                late.Tell(7, StillIn, new Seconds(10f));

                late.Tell(8, StillIn, new Seconds(10.5f)).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_out_a_joiner_out_of_their_morning()
            {
                var remote = StillIn with { InMorning = false };

                var told = OverOnDayThree().Tell(7, remote, new Seconds(1f));

                told.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_player_with_no_state()
            {
                var remote = StillIn with { HasState = false };

                var told = OverOnDayThree().Tell(7, remote, new Seconds(1f));

                told.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_player_not_connected()
            {
                var remote = StillIn with { IsPeer = false };

                var told = OverOnDayThree().Tell(7, remote, new Seconds(1f));

                told.ShouldBeFalse();
            }
        }

        public class Dawn
        {
            [Fact]
            public void Forgets_the_morning_and_whom_we_told()
            {
                var late = OverOnDayThree();
                late.Tell(7, StillIn, new Seconds(10f));

                late.Dawn();
                late.Over(3);

                late.OverDay.ShouldBe(3);
                late.Tell(7, StillIn, new Seconds(10.5f)).ShouldBeTrue();
            }
        }

        public class Clear
        {
            [Fact]
            public void Forgets_everything_when_the_session_ends()
            {
                var late = OverOnDayThree();

                late.Clear();

                late.OverDay.ShouldBe(-1);
                late.Check(DayThree).ShouldBeFalse();
            }
        }
    }
}
