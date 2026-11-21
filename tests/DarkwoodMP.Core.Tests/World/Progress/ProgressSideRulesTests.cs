using DarkwoodMP.World.Progress;

namespace DarkwoodMP.Tests.World.Progress
{
    public class ProgressSideRulesTests
    {
        /// Playing alone in a world, with a player.
        static readonly SideFacts Alone = new SideFacts
        {
            InWorld = true,
            HasPlayer = true,
        };

        /// Hosting a session in a world.
        static readonly SideFacts Host = Alone with
        {
            SessionActive = true,
            IsHost = true,
        };

        /// A client in the host's world that has had the shared state.
        static readonly SideFacts Client = Alone with
        {
            SessionActive = true,
            ClientMode = true,
            Known = true,
        };

        static Seconds T(float s) => new Seconds(s);

        public class Limits
        {
            [Fact]
            public void Pins_a_pass_a_second()
            {
                ProgressSideRules.SyncEvery.Value.ShouldBe(1f);
            }
        }

        public class Solo
        {
            [Fact]
            public void Is_solo_alone_in_a_world()
            {
                var solo = ProgressSideRules.Solo(Alone);

                solo.ShouldBeTrue();
            }

            [Theory]
            [InlineData(true, true, false)]
            [InlineData(false, false, false)]
            [InlineData(false, true, true)]
            public void Isnt_solo_in_a_session_out_of_a_world_or_as_a_client(
                bool session,
                bool inWorld,
                bool client)
            {
                var side = Alone with
                {
                    SessionActive = session,
                    InWorld = inWorld,
                    ClientMode = client,
                };

                var solo = ProgressSideRules.Solo(side);

                solo.ShouldBeFalse();
            }
        }

        public class HostSide
        {
            [Fact]
            public void Is_the_host_side_when_hosting()
            {
                var host = ProgressSideRules.HostSide(
                    Host with { InWorld = false });

                host.ShouldBeTrue();
            }

            [Fact]
            public void Is_the_host_side_alone()
            {
                var host = ProgressSideRules.HostSide(Alone);

                host.ShouldBeTrue();
            }

            [Fact]
            public void Isnt_the_host_side_as_a_client()
            {
                var host = ProgressSideRules.HostSide(Client);

                host.ShouldBeFalse();
            }
        }

        public class ClientActive
        {
            [Fact]
            public void Is_active_with_the_shared_state()
            {
                var active = ProgressSideRules.ClientActive(Client);

                active.ShouldBeTrue();
            }

            [Theory]
            [InlineData(false, true, true)]
            [InlineData(true, false, true)]
            [InlineData(true, true, false)]
            public void Isnt_active_before_it_all_holds(
                bool client,
                bool inWorld,
                bool known)
            {
                var side = Client with
                {
                    ClientMode = client,
                    InWorld = inWorld,
                    Known = known,
                };

                var active = ProgressSideRules.ClientActive(side);

                active.ShouldBeFalse();
            }
        }

        public class Cooked
        {
            [Fact]
            public void Asks_the_host_as_an_active_client()
            {
                var route = ProgressSideRules.Cooked(Client, 5);

                route.ShouldBe(ProgressRoute.Client);
            }

            [Fact]
            public void Syncs_as_the_host()
            {
                var route = ProgressSideRules.Cooked(Host, 5);

                route.ShouldBe(ProgressRoute.Host);
            }

            [Fact]
            public void Does_nothing_as_a_client_without_the_shared_state()
            {
                var route = ProgressSideRules.Cooked(
                    Client with { Known = false },
                    5);

                route.ShouldBe(ProgressRoute.None);
            }

            [Theory]
            [InlineData(0)]
            [InlineData(-1)]
            public void Does_nothing_for_no_essence(int amount)
            {
                var route = ProgressSideRules.Cooked(Host, amount);

                route.ShouldBe(ProgressRoute.None);
            }

            [Fact]
            public void Does_nothing_without_a_player()
            {
                var route = ProgressSideRules.Cooked(
                    Host with { HasPlayer = false },
                    5);

                route.ShouldBe(ProgressRoute.None);
            }
        }

        public class LevelTaken
        {
            [Fact]
            public void Sends_the_level_as_an_active_client()
            {
                var route = ProgressSideRules.LevelTaken(Client);

                route.ShouldBe(ProgressRoute.Client);
            }

            [Fact]
            public void Syncs_alone_even_without_a_player()
            {
                var route = ProgressSideRules.LevelTaken(
                    Alone with { HasPlayer = false });

                route.ShouldBe(ProgressRoute.Host);
            }

            [Fact]
            public void Does_nothing_out_of_a_world_with_no_session()
            {
                var route = ProgressSideRules.LevelTaken(
                    Alone with { InWorld = false });

                route.ShouldBe(ProgressRoute.None);
            }
        }

        public class AfterReset
        {
            [Fact]
            public void Sends_the_level_as_any_client()
            {
                var route = ProgressSideRules.AfterReset(
                    Client with { Known = false, InWorld = false });

                route.ShouldBe(ProgressRoute.Client);
            }

            [Fact]
            public void Syncs_as_anyone_else()
            {
                var route = ProgressSideRules.AfterReset(new SideFacts());

                route.ShouldBe(ProgressRoute.Host);
            }
        }

        public class Due
        {
            [Theory]
            [InlineData(0.99f, false)]
            [InlineData(1f, true)]
            [InlineData(1.5f, true)]
            public void Is_due_from_the_next_time_on(float now, bool due)
            {
                var pass = ProgressSideRules.Due(T(now), T(1f));

                pass.ShouldBe(due);
            }
        }

        public class Next
        {
            [Fact]
            public void Comes_a_second_later()
            {
                var next = ProgressSideRules.Next(T(4f));

                next.Value.ShouldBe(5f);
            }
        }

        public class Update
        {
            [Fact]
            public void Syncs_a_host_playing_together()
            {
                var tick = ProgressSideRules.Update(
                    Host with { Together = true });

                tick.Route.ShouldBe(ProgressRoute.Host);
            }

            [Fact]
            public void Leaves_a_host_with_separate_hideouts()
            {
                var tick = ProgressSideRules.Update(
                    Host with { HasRecord = true });

                tick.Route.ShouldBe(ProgressRoute.None);
            }

            [Fact]
            public void Syncs_alone_with_a_record()
            {
                var tick = ProgressSideRules.Update(
                    Alone with { HasRecord = true });

                tick.Route.ShouldBe(ProgressRoute.Host);
            }

            [Fact]
            public void Leaves_alone_without_a_record()
            {
                var tick = ProgressSideRules.Update(Alone);

                tick.Route.ShouldBe(ProgressRoute.None);
            }

            [Fact]
            public void Applies_as_an_active_client()
            {
                var tick = ProgressSideRules.Update(Client);

                tick.Route.ShouldBe(ProgressRoute.Client);
            }

            [Fact]
            public void Forgets_the_record_at_the_main_menu()
            {
                var side = new SideFacts { HasPath = true, HasRecord = true };

                var tick = ProgressSideRules.Update(side);

                tick.ShouldBe(new ProgressTick { Forgets = true });
            }

            [Fact]
            public void Keeps_the_record_in_a_session_out_of_a_world()
            {
                var side = new SideFacts
                {
                    HasPath = true,
                    SessionActive = true,
                };

                var tick = ProgressSideRules.Update(side);

                tick.Forgets.ShouldBeFalse();
            }

            [Fact]
            public void Has_nothing_to_forget_without_a_path()
            {
                var tick = ProgressSideRules.Update(new SideFacts());

                tick.Forgets.ShouldBeFalse();
            }

            [Fact]
            public void Syncs_the_host_before_applying_as_a_client()
            {
                var side = Client with { IsHost = true, Together = true };

                var tick = ProgressSideRules.Update(side);

                tick.Route.ShouldBe(ProgressRoute.Host);
            }
        }
    }
}
