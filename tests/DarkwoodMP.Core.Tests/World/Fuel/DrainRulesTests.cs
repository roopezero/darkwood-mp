using DarkwoodMP.World.Fuel;

namespace DarkwoodMP.Tests.World.Fuel
{
    public class DrainRulesTests
    {
        public class Limits
        {
            [Fact]
            public void Pins_the_two_modifiers()
            {
                var modifiers = new[]
                {
                    DrainRules.Electrician,
                    DrainRules.NoSkill,
                };

                modifiers.ShouldBe(new[] { 0.5f, 1f });
            }
        }

        public class Start
        {
            /// The host with another player in.
            static readonly DrainFacts Host = new DrainFacts
            {
                Session = true,
                Host = true,
                Player = true,
                Sharing = true,
                Proxies = true,
                Characters = true,
            };

            static readonly DrainFacts Client = new DrainFacts
            {
                Session = true,
                Client = true,
                Player = true,
                Sharing = true,
            };

            [Fact]
            public void Asks_for_the_nearest_on_the_host()
            {
                var start = DrainRules.Start(Host);

                start.ShouldBe(DrainStart.Nearest);
            }

            [Fact]
            public void Burns_nothing_on_a_sharing_client()
            {
                var start = DrainRules.Start(Client);

                start.ShouldBe(DrainStart.Zero);
            }

            [Fact]
            public void Leaves_a_client_that_isnt_sharing()
            {
                var start = DrainRules.Start(Client with { Sharing = false });

                start.ShouldBe(DrainStart.Leave);
            }

            [Fact]
            public void Leaves_a_game_with_no_session()
            {
                var start = DrainRules.Start(Host with { Session = false });

                start.ShouldBe(DrainStart.Leave);
            }

            [Fact]
            public void Leaves_the_host_with_nobody_in()
            {
                var start = DrainRules.Start(Host with { Proxies = false });

                start.ShouldBe(DrainStart.Leave);
            }

            [Fact]
            public void Leaves_the_host_with_no_characters()
            {
                var start = DrainRules.Start(Host with { Characters = false });

                start.ShouldBe(DrainStart.Leave);
            }

            [Fact]
            public void Leaves_a_game_with_no_player()
            {
                var start = DrainRules.Start(Host with { Player = false });

                start.ShouldBe(DrainStart.Leave);
            }
        }

        public class Nearest
        {
            /// A generator at the origin, the host's player 100 away.
            static readonly NearestFacts Generator = new NearestFacts
            {
                Generator = new Vec2(0f, 0f),
                Host = new Vec2(100f, 0f),
            };

            static DrainPlayer At(float x)
            {
                return new DrainPlayer { Exists = true, Pos = new Vec2(x, 0f) };
            }

            static NearestFacts With(params DrainPlayer[] players)
            {
                return Generator with { Players = players };
            }

            [Fact]
            public void Takes_a_nearer_player()
            {
                var nearest = DrainRules.Nearest(With(At(200f), At(50f)));

                nearest.ShouldBe(1);
            }

            [Fact]
            public void Takes_the_host_when_it_is_nearest()
            {
                var nearest = DrainRules.Nearest(With(At(200f)));

                nearest.ShouldBe(-1);
            }

            [Fact]
            public void Gives_a_tie_to_the_host()
            {
                var nearest = DrainRules.Nearest(With(At(-100f)));

                nearest.ShouldBe(-1);
            }

            [Fact]
            public void Gives_a_tie_to_the_first_player()
            {
                var nearest = DrainRules.Nearest(With(At(50f), At(-50f)));

                nearest.ShouldBe(0);
            }

            [Fact]
            public void Takes_any_player_when_the_host_is_away()
            {
                var drain = With(At(5000f)) with { HostAway = true };

                var nearest = DrainRules.Nearest(drain);

                nearest.ShouldBe(0);
            }

            [Fact]
            public void Takes_the_host_when_away_with_nobody_else()
            {
                var drain = With() with { HostAway = true };

                var nearest = DrainRules.Nearest(drain);

                nearest.ShouldBe(-1);
            }

            [Fact]
            public void Skips_a_player_who_is_away()
            {
                var away = At(50f) with { Away = true };

                var nearest = DrainRules.Nearest(With(away));

                nearest.ShouldBe(-1);
            }

            [Fact]
            public void Skips_a_player_in_a_location()
            {
                var inside = At(50f) with { InLocation = true };

                var nearest = DrainRules.Nearest(With(inside));

                nearest.ShouldBe(-1);
            }

            [Fact]
            public void Skips_the_test_dummy()
            {
                var dummy = At(50f) with { Dummy = true };

                var nearest = DrainRules.Nearest(With(dummy));

                nearest.ShouldBe(-1);
            }

            [Fact]
            public void Skips_a_stand_in_that_is_gone()
            {
                var gone = At(50f) with { Exists = false };

                var nearest = DrainRules.Nearest(With(gone));

                nearest.ShouldBe(-1);
            }

            [Fact]
            public void Measures_on_the_ground_plane()
            {
                var player = new DrainPlayer
                {
                    Exists = true,
                    Pos = new Vec2(60f, 79f),
                };

                var nearest = DrainRules.Nearest(With(player));

                nearest.ShouldBe(0);
            }
        }

        public class Modifier
        {
            [Theory]
            [InlineData(true, 0.5f)]
            [InlineData(false, 1f)]
            public void Halves_the_drain_for_an_electrician(
                bool electrician,
                float modifier)
            {
                var drain = DrainRules.Modifier(electrician);

                drain.ShouldBe(modifier);
            }
        }
    }
}
