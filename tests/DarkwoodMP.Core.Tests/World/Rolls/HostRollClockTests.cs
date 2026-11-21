using DarkwoodMP.World.Rolls;

namespace DarkwoodMP.Tests.World.Rolls
{
    public class HostRollClockTests
    {
        static Seconds At(float time)
        {
            return new Seconds(time);
        }

        /// A clock that sent scenario "a" and weather "w" at time 0.
        static HostRollClock SentAtZero()
        {
            var clock = new HostRollClock();
            clock.Scenario("a", At(0f));
            clock.Weather("w", At(0f));
            return clock;
        }

        public class ObjectsDue
        {
            [Fact]
            public void Waits_for_none_made()
            {
                var clock = new HostRollClock();

                clock.ObjectsDue(At(100f)).ShouldBeFalse();
            }

            [Fact]
            public void Waits_just_before_two_seconds()
            {
                var clock = new HostRollClock();
                clock.ObjectsMade(At(5f));

                clock.ObjectsDue(At(6.99f)).ShouldBeFalse();
            }

            [Fact]
            public void Sends_exactly_at_two_seconds()
            {
                var clock = new HostRollClock();
                clock.ObjectsMade(At(5f));

                clock.ObjectsDue(At(7f)).ShouldBeTrue();
            }

            [Fact]
            public void Sends_once()
            {
                var clock = new HostRollClock();
                clock.ObjectsMade(At(5f));
                clock.ObjectsDue(At(7f));

                clock.ObjectsDue(At(8f)).ShouldBeFalse();
            }

            [Fact]
            public void Moves_on_with_a_second_batch()
            {
                var clock = new HostRollClock();
                clock.ObjectsMade(At(5f));
                clock.ObjectsMade(At(6f));

                clock.ObjectsDue(At(7.5f)).ShouldBeFalse();
            }

            [Fact]
            public void Sends_objects_made_at_time_zero()
            {
                var clock = new HostRollClock();
                clock.ObjectsMade(At(0f));

                clock.ObjectsDue(At(2f)).ShouldBeTrue();
            }
        }

        public class Checks
        {
            [Fact]
            public void Looks_at_the_start()
            {
                var clock = new HostRollClock();

                clock.Checks(At(0f)).ShouldBeTrue();
            }

            [Fact]
            public void Waits_just_before_a_second()
            {
                var clock = new HostRollClock();
                clock.Checks(At(3f));

                clock.Checks(At(3.99f)).ShouldBeFalse();
            }

            [Fact]
            public void Looks_again_exactly_a_second_later()
            {
                var clock = new HostRollClock();
                clock.Checks(At(3f));

                clock.Checks(At(4f)).ShouldBeTrue();
            }
        }

        public class Scenario
        {
            [Fact]
            public void Sends_the_first_scenario_as_changed()
            {
                var clock = new HostRollClock();

                clock.Scenario("", At(0f)).ShouldBe(RollSend.Changed);
            }

            [Fact]
            public void Holds_the_same_one_before_ten_seconds()
            {
                var clock = SentAtZero();

                clock.Scenario("a", At(9.99f)).ShouldBe(RollSend.None);
            }

            [Fact]
            public void Sends_the_same_one_again_at_ten_seconds()
            {
                var clock = SentAtZero();

                clock.Scenario("a", At(10f)).ShouldBe(RollSend.Again);
            }

            [Fact]
            public void Sends_a_new_one_at_once()
            {
                var clock = SentAtZero();

                clock.Scenario("b", At(1f)).ShouldBe(RollSend.Changed);
            }

            [Fact]
            public void Counts_ten_seconds_from_the_last_send()
            {
                var clock = SentAtZero();
                clock.Scenario("b", At(5f));

                clock.Scenario("b", At(14.99f)).ShouldBe(RollSend.None);
            }

            [Fact]
            public void Keeps_its_own_time_apart_from_the_weather()
            {
                var clock = SentAtZero();
                clock.Weather("x", At(5f));

                clock.Scenario("a", At(10f)).ShouldBe(RollSend.Again);
            }
        }

        public class Weather
        {
            [Fact]
            public void Holds_the_same_weather_before_ten_seconds()
            {
                var clock = SentAtZero();

                clock.Weather("w", At(9.99f)).ShouldBe(RollSend.None);
            }

            [Fact]
            public void Sends_the_same_weather_again_at_ten_seconds()
            {
                var clock = SentAtZero();

                clock.Weather("w", At(10f)).ShouldBe(RollSend.Again);
            }

            [Fact]
            public void Sends_new_weather_at_once()
            {
                var clock = SentAtZero();

                clock.Weather("v", At(1f)).ShouldBe(RollSend.Changed);
            }
        }

        public class SessionEnded
        {
            [Fact]
            public void Sends_the_same_scenario_as_changed_after()
            {
                var clock = SentAtZero();

                clock.SessionEnded();

                clock.Scenario("a", At(1f)).ShouldBe(RollSend.Changed);
            }

            [Fact]
            public void Sends_the_same_weather_as_changed_after()
            {
                var clock = SentAtZero();

                clock.SessionEnded();

                clock.Weather("w", At(1f)).ShouldBe(RollSend.Changed);
            }

            [Fact]
            public void Drops_the_objects_due()
            {
                var clock = new HostRollClock();
                clock.ObjectsMade(At(0f));

                clock.SessionEnded();

                clock.ObjectsDue(At(5f)).ShouldBeFalse();
            }

            [Fact]
            public void Keeps_the_look_time()
            {
                var clock = new HostRollClock();
                clock.Checks(At(3f));

                clock.SessionEnded();

                clock.Checks(At(3.5f)).ShouldBeFalse();
            }
        }
    }
}
