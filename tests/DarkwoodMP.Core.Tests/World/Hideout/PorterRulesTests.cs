using DarkwoodMP.World.Hideout;

namespace DarkwoodMP.Tests.World.Hideout
{
    public class PorterRulesTests
    {
        /// The host's own transport, playing together.
        static readonly TransportFacts HostsOwn = new TransportFacts
        {
            Together = true,
            IsClient = false,
            OthersPart = false,
            JoinersPart = false,
            Replaying = false,
        };

        static readonly TransportFacts ClientsOwn = HostsOwn with
        {
            IsClient = true,
        };

        /// From hideout 1 to hideout 2, which has a package place.
        static readonly HideoutFacts OneToTwo = new HideoutFacts
        {
            HasSource = true,
            SourceId = 1,
            HasTarget = true,
            TargetBase = true,
            TargetId = 2,
            Same = false,
            HasPackagePlace = true,
            TargetName = "Hideout2",
        };

        /// A client's whistle, playing together.
        static readonly CallFacts Whistle = new CallFacts
        {
            Together = true,
            Replaying = false,
            HasPorterPlace = true,
            IsHost = false,
        };

        /// The host's own whistle made the spawner.
        static readonly CalledFacts Called = new CalledFacts
        {
            SpawnerMade = true,
            RanGame = true,
            Together = true,
            IsHost = true,
            HasPorterPlace = true,
        };

        /// A joiner's call that passes every check.
        static readonly CallCheckFacts Fine = new CallCheckFacts
        {
            HasPlace = true,
            Known = true,
            AtPlace = true,
            PlaceName = "Hideout2",
            IsDay = true,
            InTransit = false,
            Killed = false,
            PorterThere = false,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_porter_place_reach()
            {
                PorterRules.PlaceReach.ShouldBe(50f);
            }

            [Fact]
            public void Pins_the_flag_and_item_names()
            {
                var names = new[]
                {
                    PorterRules.InTransit,
                    PorterRules.Killed,
                    PorterRules.Alcohol,
                    PorterRules.Bottle,
                };

                names.ShouldBe(new[]
                {
                    "porter_inTransit",
                    "porter_killed",
                    "alcohol",
                    "bottle",
                });
            }
        }

        public class Transport
        {
            [Fact]
            public void Delivers_the_hosts_own()
            {
                var step = PorterRules.Transport(HostsOwn);

                step.ShouldBe(TransportStep.Deliver);
            }

            [Fact]
            public void Leaves_a_joiners_event_to_its_request()
            {
                var move = HostsOwn with { JoinersPart = true };

                PorterRules.Transport(move).ShouldBe(TransportStep.Nothing);
            }

            [Fact]
            public void Asks_the_host_for_a_clients_own()
            {
                var step = PorterRules.Transport(ClientsOwn);

                step.ShouldBe(TransportStep.Request);
            }

            [Fact]
            public void Waits_for_the_host_in_another_players_part()
            {
                var move = ClientsOwn with { OthersPart = true };

                PorterRules.Transport(move).ShouldBe(TransportStep.Nothing);
            }

            [Fact]
            public void Waits_for_the_host_while_replaying()
            {
                var move = ClientsOwn with { Replaying = true };

                PorterRules.Transport(move).ShouldBe(TransportStep.Nothing);
            }

            [Fact]
            public void Runs_the_games_alone()
            {
                var move = ClientsOwn with { Together = false };

                PorterRules.Transport(move).ShouldBe(TransportStep.RunGame);
            }
        }

        public class IsHideout
        {
            [Theory]
            [InlineData(2, 2, false, true)]
            [InlineData(2, 3, false, false)]
            [InlineData(2, 2, true, false)]
            [InlineData(0, 0, false, false)]
            [InlineData(-1, -1, false, false)]
            public void Finds_a_hideout_by_its_id(
                int wanted,
                int id,
                bool sub,
                bool found)
            {
                PorterRules.IsHideout(wanted, id, sub).ShouldBe(found);
            }
        }

        public class Why
        {
            [Fact]
            public void Allows_a_transport_to_another_hideout()
            {
                PorterRules.Why(OneToTwo).ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_missing_source()
            {
                var move = OneToTwo with { HasSource = false };

                PorterRules.Why(move).Reason.ShouldBe(Refusal.NoSuchHideout);
            }

            [Fact]
            public void Refuses_a_source_with_id_zero()
            {
                var move = OneToTwo with { SourceId = 0 };

                PorterRules.Why(move).Reason.ShouldBe(Refusal.NoSuchHideout);
            }

            [Fact]
            public void Refuses_no_target()
            {
                var move = OneToTwo with { HasTarget = false };

                PorterRules.Why(move).Reason.ShouldBe(Refusal.NotAtHideout);
            }

            [Fact]
            public void Refuses_a_target_that_isnt_a_hideout()
            {
                var move = OneToTwo with { TargetBase = false };

                PorterRules.Why(move).Reason.ShouldBe(Refusal.NotAtHideout);
            }

            [Fact]
            public void Refuses_a_target_with_id_zero()
            {
                var move = OneToTwo with { TargetId = 0 };

                PorterRules.Why(move).Reason.ShouldBe(Refusal.NotAtHideout);
            }

            [Fact]
            public void Refuses_the_same_hideout()
            {
                var move = OneToTwo with { Same = true };

                var verdict = PorterRules.Why(move);

                verdict.Reason.ShouldBe(Refusal.AlreadyAtHideout);
            }

            [Fact]
            public void Refuses_a_hideout_with_no_package_place()
            {
                var move = OneToTwo with { HasPackagePlace = false };

                var verdict = PorterRules.Why(move);

                verdict.Reason.ShouldBe(Refusal.NoPackagePlace);
                Refusals.Text(verdict).ShouldBe(
                    "Hideout2 has no place for the porter's package");
            }

            [Fact]
            public void Names_the_missing_source_before_the_target()
            {
                var move = OneToTwo with
                {
                    HasSource = false,
                    HasTarget = false,
                };

                PorterRules.Why(move).Reason.ShouldBe(Refusal.NoSuchHideout);
            }

            [Fact]
            public void Names_the_same_hideout_before_the_package_place()
            {
                var move = OneToTwo with
                {
                    Same = true,
                    HasPackagePlace = false,
                };

                var verdict = PorterRules.Why(move);

                verdict.Reason.ShouldBe(Refusal.AlreadyAtHideout);
            }
        }

        public class MayTransport
        {
            [Fact]
            public void Refuses_a_requester_with_no_stand_in_first()
            {
                var move = OneToTwo with { HasSource = false };

                var verdict = PorterRules.MayTransport(false, move);

                verdict.Reason.ShouldBe(Refusal.PositionUnknown);
            }

            [Fact]
            public void Checks_the_hideouts_once_the_requester_is_known()
            {
                var move = OneToTwo with { HasSource = false };

                var verdict = PorterRules.MayTransport(true, move);

                verdict.Reason.ShouldBe(Refusal.NoSuchHideout);
            }

            [Fact]
            public void Allows_a_known_requester_between_hideouts()
            {
                var verdict = PorterRules.MayTransport(true, OneToTwo);

                verdict.ShouldBe(Verdict.Allow);
            }
        }

        public class Call
        {
            [Fact]
            public void Asks_the_host_for_a_clients_whistle()
            {
                PorterRules.Call(Whistle).ShouldBe(CallStep.Request);
            }

            [Fact]
            public void Runs_the_games_on_the_host()
            {
                var call = Whistle with { IsHost = true };

                PorterRules.Call(call).ShouldBe(CallStep.RunGame);
            }

            [Fact]
            public void Runs_the_games_while_replaying()
            {
                var call = Whistle with { Replaying = true };

                PorterRules.Call(call).ShouldBe(CallStep.RunGame);
            }

            [Fact]
            public void Runs_the_games_with_no_porter_place()
            {
                var call = Whistle with { HasPorterPlace = false };

                PorterRules.Call(call).ShouldBe(CallStep.RunGame);
            }

            [Fact]
            public void Runs_the_games_alone()
            {
                var call = Whistle with { Together = false };

                PorterRules.Call(call).ShouldBe(CallStep.RunGame);
            }
        }

        public class SpawnerMade
        {
            [Theory]
            [InlineData(true, false, false, true)]
            [InlineData(false, false, false, false)]
            [InlineData(true, true, false, false)]
            [InlineData(true, false, true, false)]
            public void Copies_three_of_the_games_conditions(
                bool place,
                bool there,
                bool killed,
                bool made)
            {
                PorterRules.SpawnerMade(place, there, killed).ShouldBe(made);
            }
        }

        public class CalledHere
        {
            [Fact]
            public void Tells_the_others_of_the_hosts_call()
            {
                PorterRules.CalledHere(Called).ShouldBeTrue();
            }

            [Fact]
            public void Stays_quiet_when_no_spawner_was_made()
            {
                var called = Called with { SpawnerMade = false };

                PorterRules.CalledHere(called).ShouldBeFalse();
            }

            [Fact]
            public void Stays_quiet_when_the_game_didnt_run()
            {
                var called = Called with { RanGame = false };

                PorterRules.CalledHere(called).ShouldBeFalse();
            }

            [Fact]
            public void Stays_quiet_on_a_client()
            {
                var called = Called with { IsHost = false };

                PorterRules.CalledHere(called).ShouldBeFalse();
            }

            [Fact]
            public void Stays_quiet_alone()
            {
                var called = Called with { Together = false };

                PorterRules.CalledHere(called).ShouldBeFalse();
            }

            [Fact]
            public void Stays_quiet_with_no_porter_place()
            {
                var called = Called with { HasPorterPlace = false };

                PorterRules.CalledHere(called).ShouldBeFalse();
            }
        }

        public class NearestPorterPlace
        {
            static readonly Vec3 Origin = new Vec3(0f, 0f, 0f);

            [Fact]
            public void Finds_a_place_just_inside_the_reach()
            {
                var places = new[] { new Vec3(30f, 39.99f, 0f) };

                PorterRules.NearestPorterPlace(places, Origin).ShouldBe(0);
            }

            [Fact]
            public void Leaves_out_a_place_exactly_at_the_reach()
            {
                var places = new[] { new Vec3(30f, 40f, 0f) };

                PorterRules.NearestPorterPlace(places, Origin).ShouldBe(-1);
            }

            [Fact]
            public void Picks_the_nearest()
            {
                var places = new[]
                {
                    new Vec3(20f, 0f, 0f),
                    new Vec3(0f, 0f, 10f),
                };

                PorterRules.NearestPorterPlace(places, Origin).ShouldBe(1);
            }

            [Fact]
            public void Keeps_the_first_of_two_as_near()
            {
                var places = new[]
                {
                    new Vec3(10f, 0f, 0f),
                    new Vec3(-10f, 0f, 0f),
                };

                PorterRules.NearestPorterPlace(places, Origin).ShouldBe(0);
            }

            [Fact]
            public void Finds_none_in_an_empty_list()
            {
                var places = new Vec3[0];

                PorterRules.NearestPorterPlace(places, Origin).ShouldBe(-1);
            }
        }

        public class MayCall
        {
            [Fact]
            public void Allows_a_call_that_passes_every_check()
            {
                PorterRules.MayCall(Fine).ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Words_the_place_as_the_host_does()
            {
                var call = Fine with { AtPlace = false };

                var verdict = PorterRules.MayCall(call);

                verdict.Reason.ShouldBe(Refusal.NotAtPlace);
                Refusals.Text(verdict).ShouldBe("they aren't at Hideout2");
            }

            public static TheoryData<CallCheckFacts, Refusal> Refused =>
                new TheoryData<CallCheckFacts, Refusal>
                {
                    { Fine with { HasPlace = false }, Refusal.NoPorterPlace },
                    { Fine with { Known = false }, Refusal.PositionUnknown },
                    { Fine with { IsDay = false }, Refusal.NotDaytime },
                    {
                        Fine with { InTransit = true },
                        Refusal.PorterOnTheWay
                    },
                    { Fine with { Killed = true }, Refusal.PorterDead },
                    { Fine with { PorterThere = true }, Refusal.PorterThere },
                };

            [Theory]
            [MemberData(nameof(Refused))]
            public void Refuses_each_failed_check(
                CallCheckFacts call,
                Refusal reason)
            {
                PorterRules.MayCall(call).Reason.ShouldBe(reason);
            }

            /// Each pair fails two checks; the first in the host's order
            /// wins.
            public static TheoryData<CallCheckFacts, Refusal> Both =>
                new TheoryData<CallCheckFacts, Refusal>
                {
                    {
                        Fine with { HasPlace = false, Known = false },
                        Refusal.NoPorterPlace
                    },
                    {
                        Fine with { Known = false, AtPlace = false },
                        Refusal.PositionUnknown
                    },
                    {
                        Fine with { AtPlace = false, IsDay = false },
                        Refusal.NotAtPlace
                    },
                    {
                        Fine with { IsDay = false, InTransit = true },
                        Refusal.NotDaytime
                    },
                    {
                        Fine with { InTransit = true, Killed = true },
                        Refusal.PorterOnTheWay
                    },
                    {
                        Fine with { Killed = true, PorterThere = true },
                        Refusal.PorterDead
                    },
                };

            [Theory]
            [MemberData(nameof(Both))]
            public void Gives_the_first_refusal_in_order(
                CallCheckFacts call,
                Refusal reason)
            {
                PorterRules.MayCall(call).Reason.ShouldBe(reason);
            }
        }
    }
}
