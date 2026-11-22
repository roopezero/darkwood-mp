using DarkwoodMP.World;

namespace DarkwoodMP.Tests.World
{
    public class SharedIdRulesTests
    {
        const int Scene = 1;

        static readonly JoinerIds None = SharedIdRules.Reset(Scene);

        /// Two joiners let in to this world, with counters 500 and 800.
        static readonly JoinerIds Two = SharedIdRules.Joined(
            SharedIdRules.Joined(None, 500, Scene),
            800,
            Scene);

        /// A host with those two joiners, whose own save's counter is 300.
        static readonly IdLimitFacts Hosting = new IdLimitFacts
        {
            Host = true,
            Joiners = Two,
            Loaded = 300,
        };

        public class SendLimit
        {
            [Fact]
            public void Gives_a_client_the_counter_it_loaded()
            {
                var client = Hosting with { Host = false };

                var limit = SharedIdRules.SendLimit(client);

                limit.ShouldBe(300);
            }

            [Fact]
            public void Gives_a_host_with_no_joiner_0()
            {
                var alone = Hosting with { Joiners = None };

                var limit = SharedIdRules.SendLimit(alone);

                limit.ShouldBe(0);
            }

            [Fact]
            public void Ignores_the_lowest_while_no_joiner_counts()
            {
                var start = new JoinerIds { Min = int.MaxValue, Scene = Scene };
                var alone = Hosting with { Joiners = start };

                var limit = SharedIdRules.SendLimit(alone);

                limit.ShouldBe(0);
            }

            [Fact]
            public void Gives_a_host_the_lowest_joiner_counter()
            {
                var limit = SharedIdRules.SendLimit(Hosting);

                limit.ShouldBe(500);
            }
        }

        public class ResolveLimit
        {
            [Fact]
            public void Gives_a_client_the_counter_it_loaded()
            {
                var client = Hosting with { Host = false };

                var limit = SharedIdRules.ResolveLimit(client);

                limit.ShouldBe(300);
            }

            [Fact]
            public void Gives_a_host_with_no_joiner_0()
            {
                var alone = Hosting with { Joiners = None };

                var limit = SharedIdRules.ResolveLimit(alone);

                limit.ShouldBe(0);
            }

            [Fact]
            public void Gives_a_host_the_highest_joiner_counter()
            {
                var limit = SharedIdRules.ResolveLimit(Hosting);

                limit.ShouldBe(800);
            }
        }

        public class Joined
        {
            [Fact]
            public void Takes_the_first_joiner_as_lowest_and_highest()
            {
                var ids = SharedIdRules.Joined(None, 500, Scene);

                ids.Min.ShouldBe(500);
                ids.Max.ShouldBe(500);
            }

            [Fact]
            public void Lowers_the_lowest()
            {
                var ids = SharedIdRules.Joined(Two, 200, Scene);

                ids.Min.ShouldBe(200);
                ids.Max.ShouldBe(800);
            }

            [Fact]
            public void Raises_the_highest()
            {
                var ids = SharedIdRules.Joined(Two, 900, Scene);

                ids.Min.ShouldBe(500);
                ids.Max.ShouldBe(900);
            }

            [Theory]
            [InlineData(0)]
            [InlineData(-1)]
            public void Ignores_a_counter_of_0_or_less(int n)
            {
                var ids = SharedIdRules.Joined(Two, n, Scene);

                ids.ShouldBe(Two);
            }

            [Fact]
            public void Counts_a_counter_of_1()
            {
                var ids = SharedIdRules.Joined(None, 1, Scene);

                ids.Max.ShouldBe(1);
            }

            [Fact]
            public void Starts_again_in_a_new_scene()
            {
                var ids = SharedIdRules.Joined(Two, 900, 2);

                ids.ShouldBe(new JoinerIds { Min = 900, Max = 900, Scene = 2 });
            }

            [Fact]
            public void Starts_again_in_a_new_scene_before_any_joiner()
            {
                var ids = SharedIdRules.Joined(None, 500, 2);

                ids.ShouldBe(new JoinerIds { Min = 500, Max = 500, Scene = 2 });
            }

            [Fact]
            public void Keeps_the_old_scene_for_a_counter_of_0()
            {
                var ids = SharedIdRules.Joined(Two, 0, 2);

                ids.Scene.ShouldBe(Scene);
            }
        }

        public class Reset
        {
            [Fact]
            public void Leaves_no_joiner_for_the_scene()
            {
                var ids = SharedIdRules.Reset(4);

                ids.ShouldBe(new JoinerIds { Scene = 4 });
            }
        }
    }
}
