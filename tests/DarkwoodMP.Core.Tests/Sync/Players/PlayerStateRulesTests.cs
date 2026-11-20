using DarkwoodMP.Sync.Players;

namespace DarkwoodMP.Tests.Sync.Players
{
    public class PlayerStateRulesTests
    {
        const ulong HostId = 1;
        const ulong Us = 2;
        const ulong Other = 3;

        /// The host gets a state from a client, about someone else.
        static readonly StateFacts OnHost = new StateFacts
        {
            Host = true,
            From = Other,
            Said = 99,
            HostId = HostId,
            LocalId = HostId,
        };

        /// A client gets the host's relay of another client's state.
        static readonly StateFacts OnClient = new StateFacts
        {
            From = HostId,
            Said = Other,
            HostId = HostId,
            LocalId = Us,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_state_limits()
            {
                var limits = new[]
                {
                    PlayerStateRules.LeftIgnore,
                    PlayerStateRules.MinSendRate,
                };

                var pinned = new[] { 5f, 1f };

                limits.ShouldBe(pinned);
            }
        }

        public class SendInterval
        {
            [Theory]
            [InlineData(20, 0.05f)]
            [InlineData(1, 1f)]
            [InlineData(0, 1f)]
            [InlineData(-3, 1f)]
            public void Sends_at_least_once_a_second(int rate, float every)
            {
                var interval = PlayerStateRules.SendInterval(rate);

                interval.Value.ShouldBe(every, 1e-6f);
            }
        }

        public class SendDue
        {
            [Fact]
            public void Sends_exactly_when_due()
            {
                var due = PlayerStateRules.SendDue(
                    new Seconds(1f), new Seconds(1f));

                due.ShouldBeTrue();
            }

            [Fact]
            public void Waits_until_due()
            {
                var due = PlayerStateRules.SendDue(
                    new Seconds(0.99f), new Seconds(1f));

                due.ShouldBeFalse();
            }
        }

        public class MaySend
        {
            static readonly SendFacts Connected = new SendFacts
            {
                Active = true,
                Connected = true,
            };

            [Fact]
            public void Sends_in_a_connected_session()
            {
                var sends = PlayerStateRules.MaySend(Connected);

                sends.ShouldBeTrue();
            }

            [Fact]
            public void Holds_it_out_of_an_active_session()
            {
                var send = Connected with { Active = false };

                var sends = PlayerStateRules.MaySend(send);

                sends.ShouldBeFalse();
            }

            [Fact]
            public void Holds_it_while_not_connected()
            {
                var send = Connected with { Connected = false };

                var sends = PlayerStateRules.MaySend(send);

                sends.ShouldBeFalse();
            }

            [Fact]
            public void Holds_it_while_our_character_is_placed()
            {
                var send = Connected with { Placing = true };

                var sends = PlayerStateRules.MaySend(send);

                sends.ShouldBeFalse();
            }
        }

        public class GuardKey
        {
            [Fact]
            public void Guards_by_the_sender_on_the_host()
            {
                var key = PlayerStateRules.GuardKey(true, Other, 99);

                key.ShouldBe(Other);
            }

            [Fact]
            public void Guards_by_the_owner_said_on_a_client()
            {
                var key = PlayerStateRules.GuardKey(false, HostId, Other);

                key.ShouldBe(Other);
            }
        }

        public class Arrive
        {
            [Fact]
            public void Takes_the_sender_as_owner_on_the_host()
            {
                var arrival = PlayerStateRules.Arrive(OnHost);

                arrival.Owner.ShouldBe(Other);
            }

            [Fact]
            public void Checks_the_move_on_the_host()
            {
                var arrival = PlayerStateRules.Arrive(OnHost);

                arrival.Move.ShouldBe(StateMove.Check);
            }

            [Fact]
            public void Forgets_a_spectators_move_on_the_host()
            {
                var state = OnHost with { Spectating = true };

                var arrival = PlayerStateRules.Arrive(state);

                arrival.Move.ShouldBe(StateMove.Forget);
            }

            [Fact]
            public void Relays_on_the_host()
            {
                var arrival = PlayerStateRules.Arrive(OnHost);

                arrival.Relays.ShouldBeTrue();
            }

            [Fact]
            public void Shows_a_clients_state_on_the_host()
            {
                var arrival = PlayerStateRules.Arrive(OnHost);

                arrival.Drop.ShouldBe(StateDrop.None);
            }

            [Fact]
            public void Drops_a_state_of_a_player_who_left_just_now()
            {
                var state = OnHost with { LeftJustNow = true };

                var arrival = PlayerStateRules.Arrive(state);

                arrival.Drop.ShouldBe(StateDrop.LeftJustNow);
            }

            [Fact]
            public void Neither_checks_nor_relays_one_who_left_just_now()
            {
                var state = OnHost with { LeftJustNow = true };

                var arrival = PlayerStateRules.Arrive(state);

                (arrival.Move, arrival.Relays)
                    .ShouldBe((StateMove.None, false));
            }

            [Fact]
            public void Shows_the_hosts_relay_on_a_client()
            {
                var arrival = PlayerStateRules.Arrive(OnClient);

                (arrival.Owner, arrival.Drop)
                    .ShouldBe((Other, StateDrop.None));
            }

            [Fact]
            public void Neither_checks_nor_relays_on_a_client()
            {
                var arrival = PlayerStateRules.Arrive(OnClient);

                (arrival.Move, arrival.Relays)
                    .ShouldBe((StateMove.None, false));
            }

            [Fact]
            public void Drops_a_state_not_from_the_host_on_a_client()
            {
                var state = OnClient with { From = Other };

                var arrival = PlayerStateRules.Arrive(state);

                arrival.Drop.ShouldBe(StateDrop.NotFromHost);
            }

            [Fact]
            public void Drops_a_state_about_us_on_a_client()
            {
                var state = OnClient with { Said = Us };

                var arrival = PlayerStateRules.Arrive(state);

                arrival.Drop.ShouldBe(StateDrop.AboutUs);
            }

            [Fact]
            public void Prefers_left_just_now_to_not_from_the_host()
            {
                var state = OnClient with
                {
                    From = Other,
                    LeftJustNow = true,
                };

                var arrival = PlayerStateRules.Arrive(state);

                arrival.Drop.ShouldBe(StateDrop.LeftJustNow);
            }

            [Fact]
            public void Prefers_not_from_the_host_to_about_us()
            {
                var state = OnClient with { From = Other, Said = Us };

                var arrival = PlayerStateRules.Arrive(state);

                arrival.Drop.ShouldBe(StateDrop.NotFromHost);
            }

            [Fact]
            public void Relays_but_drops_a_state_from_ourselves_on_the_host()
            {
                var state = OnHost with { From = HostId };

                var arrival = PlayerStateRules.Arrive(state);

                (arrival.Relays, arrival.Drop)
                    .ShouldBe((true, StateDrop.AboutUs));
            }
        }
    }
}
