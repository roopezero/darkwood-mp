using DarkwoodMP.World.Scripted;

namespace DarkwoodMP.Tests.World.Scripted
{
    public class TripRulesTests
    {
        /// A trip to a target with no delay and the game's default fade.
        static readonly TripStep Trip = new TripStep
        {
            Exists = true,
            Trip = true,
            HasTarget = true,
            Fade = new Seconds(0.5f),
        };

        /// A step that is no trip and waits two seconds.
        static readonly TripStep Wait = new TripStep
        {
            Exists = true,
            Delay = new Seconds(2f),
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_margin_and_the_shortest_fade()
            {
                var limits = new[] { TripRules.Margin, TripRules.MinFade };

                limits.ShouldBe(new[] { 10f, 0.5f });
            }
        }

        public class Waits
        {
            [Fact]
            public void Expects_a_trip_for_the_margin_and_its_fade()
            {
                var waits = TripRules.Waits(new[] { Trip });

                waits.Count.ShouldBe(1);
                waits[0].Step.ShouldBe(0);
                waits[0].Wait.Value.ShouldBe(10.5f);
            }

            [Fact]
            public void Adds_the_delays_up_to_and_with_the_trip()
            {
                var trip = Trip with { Delay = new Seconds(1f) };

                var waits = TripRules.Waits(new[] { Wait, trip });

                waits[0].Step.ShouldBe(1);
                waits[0].Wait.Value.ShouldBe(13.5f);
            }

            [Fact]
            public void Leaves_out_a_negative_delay()
            {
                var back = Wait with { Delay = new Seconds(-5f) };

                var waits = TripRules.Waits(new[] { back, Trip });

                waits[0].Wait.Value.ShouldBe(10.5f);
            }

            [Fact]
            public void Leaves_out_a_disabled_steps_delay()
            {
                var off = Wait with { Disabled = true };

                var waits = TripRules.Waits(new[] { off, Trip });

                waits[0].Wait.Value.ShouldBe(10.5f);
            }

            [Fact]
            public void Leaves_out_a_missing_steps_delay()
            {
                var gone = Wait with { Exists = false };

                var waits = TripRules.Waits(new[] { gone, Trip });

                waits[0].Wait.Value.ShouldBe(10.5f);
            }

            [Fact]
            public void Takes_a_fade_just_past_the_shortest()
            {
                var trip = Trip with { Fade = new Seconds(0.75f) };

                var waits = TripRules.Waits(new[] { trip });

                waits[0].Wait.Value.ShouldBe(10.75f);
            }

            [Fact]
            public void Takes_the_shortest_fade_for_a_shorter_one()
            {
                var trip = Trip with { Fade = new Seconds(0f) };

                var waits = TripRules.Waits(new[] { trip });

                waits[0].Wait.Value.ShouldBe(10.5f);
            }

            [Fact]
            public void Counts_a_trip_with_no_target_in_the_delays_only()
            {
                var lost = Trip with
                {
                    HasTarget = false,
                    Delay = new Seconds(3f),
                };

                var waits = TripRules.Waits(new[] { lost, Trip });

                waits.Count.ShouldBe(1);
                waits[0].Wait.Value.ShouldBe(13.5f);
            }

            [Fact]
            public void Expects_nothing_without_trips()
            {
                var waits = TripRules.Waits(new[] { Wait, Wait });

                waits.ShouldBeEmpty();
            }

            [Fact]
            public void Expects_each_trip_with_the_delays_so_far()
            {
                var waits = TripRules.Waits(new[] { Trip, Wait, Trip });

                waits.Count.ShouldBe(2);
                waits[1].Step.ShouldBe(2);
                waits[1].Wait.Value.ShouldBe(12.5f);
            }
        }
    }
}
