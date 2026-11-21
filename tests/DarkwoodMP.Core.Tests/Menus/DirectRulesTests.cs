using DarkwoodMP.Menus;

namespace DarkwoodMP.Tests.Menus
{
    public class DirectRulesTests
    {
        /// Hosting a Direct game.
        static readonly DirectFacts Hosting = new DirectFacts
        {
            Active = true,
            OnTcp = true,
            Host = true,
            TcpBusy = true,
        };

        /// A router that forwards the port, on the internet itself.
        static readonly InternetFacts Forwarded = new InternetFacts
        {
            Mapped = true,
            RouterIp = "203.0.113.7",
            RouterPort = 27016,
            Port = 27015,
        };

        static readonly InternetFacts LookedUp = new InternetFacts
        {
            PublicIp = "198.51.100.2",
            Port = 27015,
        };

        public class View
        {
            [Fact]
            public void Shows_a_Direct_game()
            {
                var view = DirectRules.View(Hosting);

                view.ShouldBe(DirectView.DirectGame);
            }

            [Fact]
            public void Shows_a_Steam_host_their_game()
            {
                var direct = Hosting with { OnTcp = false, TcpBusy = false };

                var view = DirectRules.View(direct);

                view.ShouldBe(DirectView.SteamHost);
            }

            [Fact]
            public void Shows_a_Steam_joiner_their_game()
            {
                var direct = Hosting with { OnTcp = false, Host = false };

                var view = DirectRules.View(direct);

                view.ShouldBe(DirectView.SteamClient);
            }

            [Fact]
            public void Puts_a_Steam_game_before_the_TCP_backend()
            {
                var direct = Hosting with { OnTcp = false, TcpBusy = true };

                var view = DirectRules.View(direct);

                view.ShouldBe(DirectView.SteamHost);
            }

            [Fact]
            public void Shows_a_Direct_join_still_connecting()
            {
                var direct = new DirectFacts { TcpBusy = true };

                var view = DirectRules.View(direct);

                view.ShouldBe(DirectView.DirectGame);
            }

            [Fact]
            public void Shows_the_idle_section_in_no_game()
            {
                var view = DirectRules.View(default);

                view.ShouldBe(DirectView.Idle);
            }

            [Fact]
            public void Shows_the_idle_section_for_a_TCP_session_gone_quiet()
            {
                var direct = Hosting with { TcpBusy = false };

                var view = DirectRules.View(direct);

                view.ShouldBe(DirectView.Idle);
            }
        }

        public class IdleLine
        {
            [Fact]
            public void Says_the_host_s_refusal_first()
            {
                var line = DirectRules.IdleLine("The server is full.", "x");

                line.ShouldBe("Refused: The server is full.");
            }

            [Fact]
            public void Says_the_status_otherwise()
            {
                var line = DirectRules.IdleLine(null, "Connection lost");

                line.ShouldBe("Connection lost");
            }

            [Fact]
            public void Says_nothing_when_not_connected()
            {
                var line = DirectRules.IdleLine(null, "Not connected");

                line.ShouldBeNull();
            }

            [Fact]
            public void Says_an_empty_refusal()
            {
                var line = DirectRules.IdleLine("", "Not connected");

                line.ShouldBe("Refused: ");
            }
        }

        public class Internet
        {
            [Fact]
            public void Shares_the_router_s_address_and_port()
            {
                var row = DirectRules.Internet(Forwarded);

                row.Address.ShouldBe("203.0.113.7:27016");
                row.Note.ShouldBe("internet");
            }

            [Fact]
            public void Prefers_the_router_to_a_looked_up_address()
            {
                var net = Forwarded with { PublicIp = "198.51.100.2" };

                var row = DirectRules.Internet(net);

                row.Address.ShouldBe("203.0.113.7:27016");
            }

            [Fact]
            public void Shares_a_looked_up_address_behind_another_router()
            {
                var net = Forwarded with
                {
                    BehindAnotherNat = true,
                    PublicIp = "198.51.100.2",
                };

                var row = DirectRules.Internet(net);

                row.Address.ShouldBe("198.51.100.2:27015");
                row.Note.ShouldBe("internet");
            }

            [Fact]
            public void Says_to_forward_the_port_when_it_is_not()
            {
                var row = DirectRules.Internet(LookedUp);

                row.Address.ShouldBe("198.51.100.2:27015");
                row.Note.ShouldBe("internet: forward the port");
            }

            [Fact]
            public void Shares_a_looked_up_address_when_the_router_has_none()
            {
                var net = Forwarded with
                {
                    RouterIp = null,
                    PublicIp = "198.51.100.2",
                };

                var row = DirectRules.Internet(net);

                row.Address.ShouldBe("198.51.100.2:27015");
                row.Note.ShouldBe("internet");
            }

            [Fact]
            public void Has_no_address_before_the_look_up()
            {
                var net = Forwarded with { BehindAnotherNat = true };

                var row = DirectRules.Internet(net);

                row.Address.ShouldBeNull();
            }
        }

        public class MayReload
        {
            [Theory]
            [InlineData(false, true, true)]
            [InlineData(false, false, false)]
            [InlineData(true, true, false)]
            public void Lets_a_connected_joiner_reload(
                bool host,
                bool connected,
                bool may)
            {
                var result = DirectRules.MayReload(host, connected);

                result.ShouldBe(may);
            }
        }
    }
}
