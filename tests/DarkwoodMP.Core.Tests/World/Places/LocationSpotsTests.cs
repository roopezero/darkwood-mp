using DarkwoodMP.World.Places;

namespace DarkwoodMP.Tests.World.Places
{
    public class LocationSpotsTests
    {
        /// A connected client in a world.
        static readonly WaitFacts Client = new WaitFacts
        {
            IsClient = true,
            Connected = true,
            InWorld = true,
        };

        static readonly Spot Hosts = new Spot
        {
            Pos = new Vec3(-75000f, 0f, 80000f),
            Turn = 180,
        };

        public class Ask
        {
            [Fact]
            public void Asks_once_for_a_name()
            {
                var spots = new LocationSpots();

                spots.Ask("Bunker").ShouldBeTrue();
                spots.Ask("Bunker").ShouldBeFalse();
            }

            [Fact]
            public void Doesnt_ask_again_after_an_answer()
            {
                var spots = new LocationSpots();
                spots.Ask("Bunker");

                spots.Answered("Bunker", Hosts);

                spots.Ask("Bunker").ShouldBeFalse();
            }

            [Fact]
            public void Never_asks_again_for_a_name_given_up()
            {
                var spots = new LocationSpots();
                spots.Ask("Bunker");

                spots.GiveUp("Bunker");

                spots.Ask("Bunker").ShouldBeFalse();
            }

            [Fact]
            public void Doesnt_ask_for_a_known_spot()
            {
                var spots = new LocationSpots();
                spots.Promise("Bunker", Hosts);

                spots.Ask("Bunker").ShouldBeFalse();
            }
        }

        public class MustWait
        {
            [Fact]
            public void Waits_for_an_unknown_spot()
            {
                new LocationSpots().MustWait(Client, "Bunker").ShouldBeTrue();
            }

            [Fact]
            public void Stops_waiting_once_answered()
            {
                var spots = new LocationSpots();

                spots.Answered("Bunker", Hosts);

                spots.MustWait(Client, "Bunker").ShouldBeFalse();
            }

            [Fact]
            public void Stops_waiting_once_given_up()
            {
                var spots = new LocationSpots();

                spots.GiveUp("Bunker");

                spots.MustWait(Client, "Bunker").ShouldBeFalse();
            }

            [Fact]
            public void Keeps_a_give_up_after_a_late_answer()
            {
                var spots = new LocationSpots();
                spots.GiveUp("Bunker");

                spots.Answered("Bunker", Hosts);

                spots.GaveUpOn("Bunker", "Bunker").ShouldBeTrue();
            }

            [Fact]
            public void Never_waits_on_the_host()
            {
                var wait = Client with { IsClient = false };

                new LocationSpots().MustWait(wait, "Bunker").ShouldBeFalse();
            }

            [Fact]
            public void Never_waits_disconnected()
            {
                var wait = Client with { Connected = false };

                new LocationSpots().MustWait(wait, "Bunker").ShouldBeFalse();
            }

            [Fact]
            public void Never_waits_out_of_a_world()
            {
                var wait = Client with { InWorld = false };

                new LocationSpots().MustWait(wait, "Bunker").ShouldBeFalse();
            }
        }

        public class GaveUpOn
        {
            [Theory]
            [InlineData("Bunker_1", "Bunker", true)]
            [InlineData("Bunker", "Bunker_1", true)]
            [InlineData("Church", "Church", false)]
            public void Matches_the_name_or_the_true_name(
                string name,
                string trueName,
                bool gaveUp)
            {
                var spots = new LocationSpots();
                spots.GiveUp("Bunker");

                spots.GaveUpOn(name, trueName).ShouldBe(gaveUp);
            }
        }

        public class TrySpotFor
        {
            [Fact]
            public void Gives_the_known_spot()
            {
                var spots = new LocationSpots();
                spots.Promise("Bunker", Hosts);

                spots.TrySpotFor(true, "Bunker", out var spot).ShouldBeTrue();

                spot.ShouldBe(Hosts);
            }

            [Fact]
            public void Gives_none_without_an_active_session()
            {
                var spots = new LocationSpots();
                spots.Promise("Bunker", Hosts);

                spots.TrySpotFor(false, "Bunker", out _).ShouldBeFalse();
            }

            [Fact]
            public void Gives_none_for_an_unknown_name()
            {
                var spots = new LocationSpots();

                spots.TrySpotFor(true, "Bunker", out _).ShouldBeFalse();
            }

            [Fact]
            public void Gives_none_with_no_name()
            {
                var spots = new LocationSpots();

                spots.TrySpotFor(true, null, out _).ShouldBeFalse();
            }
        }

        public class TurnFor
        {
            [Fact]
            public void Uses_the_hosts_turn_over_the_roll()
            {
                var spots = new LocationSpots();
                spots.Promise("Bunker", Hosts);

                spots.TurnFor(true, "Bunker", 0).ShouldBe(180);
            }

            [Fact]
            public void Uses_the_roll_for_an_unknown_name()
            {
                new LocationSpots().TurnFor(true, "Bunker", 0).ShouldBe(0);
            }

            [Fact]
            public void Uses_the_roll_without_an_active_session()
            {
                var spots = new LocationSpots();
                spots.Promise("Bunker", Hosts);

                spots.TurnFor(false, "Bunker", 0).ShouldBe(0);
            }

            [Fact]
            public void Uses_the_roll_with_no_name()
            {
                new LocationSpots().TurnFor(true, null, 90).ShouldBe(90);
            }
        }

        public class Clear
        {
            [Fact]
            public void Forgets_spots_asks_and_give_ups()
            {
                var spots = new LocationSpots();
                spots.Promise("Bunker", Hosts);
                spots.Ask("Church");
                spots.GiveUp("Mill");

                spots.Clear();

                spots.TryGet("Bunker", out _).ShouldBeFalse();
                spots.Ask("Church").ShouldBeTrue();
                spots.GaveUpOn("Mill", "Mill").ShouldBeFalse();
            }
        }
    }
}
