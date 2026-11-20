using DarkwoodMP.World.Places;

namespace DarkwoodMP.Tests.World.Places
{
    public class LocationSlotRulesTests
    {
        /// The host in a world, asked for a real location it hasn't got.
        static readonly AskFacts NewOne = new AskFacts
        {
            IsHost = true,
            InWorld = true,
            HasLocations = true,
            Name = "Bunker",
            Known = false,
            Loaded = false,
            Real = true,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_ask_timeout()
            {
                LocationSlotRules.AskTimeout.ShouldBe(10f);
            }

            [Fact]
            public void Pins_the_spot_counts()
            {
                var counts = new[]
                {
                    LocationSlotRules.GameSpots,
                    LocationSlotRules.Margin,
                    LocationSlotRules.RowLength,
                };

                counts.ShouldBe(new[] { 21, 8, 7 });
            }
        }

        public class Answer
        {
            [Fact]
            public void Gives_a_new_spot_for_a_real_location()
            {
                LocationSlotRules.Answer(NewOne).ShouldBe(AskAnswer.NewSpot);
            }

            [Fact]
            public void Gives_the_known_spot_first()
            {
                var ask = NewOne with { Known = true, Loaded = true };

                LocationSlotRules.Answer(ask).ShouldBe(AskAnswer.Known);
            }

            [Fact]
            public void Gives_where_our_copy_stands()
            {
                var ask = NewOne with { Loaded = true, Real = false };

                LocationSlotRules.Answer(ask).ShouldBe(AskAnswer.Loaded);
            }

            [Fact]
            public void Gives_no_answer_for_a_name_not_in_the_build()
            {
                var ask = NewOne with { Real = false };

                var answer = LocationSlotRules.Answer(ask);

                answer.ShouldBe(AskAnswer.NoSuchLocation);
            }

            public static TheoryData<AskFacts> Ignored =>
                new TheoryData<AskFacts>
                {
                    NewOne with { IsHost = false, Known = true },
                    NewOne with { InWorld = false, Known = true },
                    NewOne with { HasLocations = false, Known = true },
                    NewOne with { Name = "", Known = true },
                    NewOne with { Name = null, Known = true },
                };

            [Theory]
            [MemberData(nameof(Ignored))]
            public void Ignores_an_ask_it_cant_answer(AskFacts ask)
            {
                LocationSlotRules.Answer(ask).ShouldBe(AskAnswer.Ignore);
            }
        }

        public class LoadsHere
        {
            [Theory]
            [InlineData(false, true)]
            [InlineData(true, false)]
            public void Loads_a_new_location_but_not_a_dream(
                bool dream,
                bool loads)
            {
                LocationSlotRules.LoadsHere(dream).ShouldBe(loads);
            }
        }

        public class AsksForDream
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(false, true, true)]
            [InlineData(false, false, false)]
            public void Marks_an_ask_from_a_dream(
                bool prepared,
                bool dreaming,
                bool dream)
            {
                var asks = LocationSlotRules.AsksForDream(prepared, dreaming);

                asks.ShouldBe(dream);
            }
        }

        public class Need
        {
            [Fact]
            public void Keeps_eight_spots_past_the_count()
            {
                LocationSlotRules.Need(13).ShouldBe(21);
            }
        }

        public class ExtraSpot
        {
            [Theory]
            [InlineData(21, -75000f, 80000f)]
            [InlineData(27, 75000f, 80000f)]
            [InlineData(28, -75000f, -110000f)]
            [InlineData(35, -75000f, 110000f)]
            [InlineData(42, -75000f, -140000f)]
            [InlineData(49, -75000f, 140000f)]
            public void Adds_rows_north_and_south_in_turn(
                int count,
                float x,
                float z)
            {
                var spot = LocationSlotRules.ExtraSpot(count);

                spot.X.ShouldBe(x);
                spot.Y.ShouldBe(0f);
                spot.Z.ShouldBe(z);
            }

            [Fact]
            public void Gives_a_short_list_the_first_extra_spot()
            {
                var spot = LocationSlotRules.ExtraSpot(5);

                spot.X.ShouldBe(-75000f);
                spot.Z.ShouldBe(80000f);
            }
        }

        public class TurnOf
        {
            [Theory]
            [InlineData(89.6f, 90)]
            [InlineData(0.5f, 0)]
            [InlineData(1.5f, 2)]
            [InlineData(180.5f, 180)]
            [InlineData(270.4f, 270)]
            public void Rounds_halves_to_even(float y, int turn)
            {
                LocationSlotRules.TurnOf(y).ShouldBe(turn);
            }
        }

        public class Waits
        {
            static readonly Seconds Until = new Seconds(30f);

            [Fact]
            public void Waits_just_before_the_timeout()
            {
                var now = new Seconds(29.99f);

                LocationSlotRules.Waits(true, now, Until).ShouldBeTrue();
            }

            [Fact]
            public void Stops_exactly_at_the_timeout()
            {
                var now = new Seconds(30f);

                LocationSlotRules.Waits(true, now, Until).ShouldBeFalse();
            }

            [Fact]
            public void Stops_once_it_needn_t_wait()
            {
                var now = new Seconds(1f);

                LocationSlotRules.Waits(false, now, Until).ShouldBeFalse();
            }

            [Fact]
            public void Waits_ten_seconds()
            {
                var until = LocationSlotRules.WaitUntil(new Seconds(20f));

                until.Value.ShouldBe(30f);
            }
        }

        public class AsksEarly
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(true, true, false)]
            [InlineData(false, false, false)]
            public void Asks_as_the_trip_starts(
                bool wait,
                bool loaded,
                bool asks)
            {
                LocationSlotRules.AsksEarly(wait, loaded).ShouldBe(asks);
            }
        }

        public class PromisesOwn
        {
            [Theory]
            [InlineData(true, true, "Bunker", true)]
            [InlineData(true, false, "Bunker", false)]
            [InlineData(false, true, "Bunker", false)]
            [InlineData(true, true, null, false)]
            public void Promises_the_hosts_own_load(
                bool active,
                bool host,
                string name,
                bool promises)
            {
                var own = LocationSlotRules.PromisesOwn(active, host, name);

                own.ShouldBe(promises);
            }
        }
    }
}
