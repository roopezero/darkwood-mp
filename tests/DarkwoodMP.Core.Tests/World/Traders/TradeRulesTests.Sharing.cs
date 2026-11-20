using DarkwoodMP.World.Traders;

namespace DarkwoodMP.Tests.World.Traders
{
    /// How a trade is told apart, shared and undone.
    public partial class TradeRulesTests
    {
        public class Accepted
        {
            /// Two types were in the windows, and both are empty after.
            static readonly AcceptFacts Emptied = new AcceptFacts
            {
                Counted = true,
                Took = 1,
                Gave = 1,
            };

            [Fact]
            public void Takes_emptied_windows_as_a_trade()
            {
                TradeRules.Accepted(Emptied).ShouldBeTrue();
            }

            [Fact]
            public void Takes_a_trade_that_only_took()
            {
                var accept = Emptied with { Gave = 0 };

                TradeRules.Accepted(accept).ShouldBeTrue();
            }

            [Fact]
            public void Ignores_windows_it_didnt_count()
            {
                var accept = Emptied with { Counted = false };

                TradeRules.Accepted(accept).ShouldBeFalse();
            }

            [Fact]
            public void Ignores_windows_that_held_nothing()
            {
                var accept = Emptied with { Took = 0, Gave = 0 };

                TradeRules.Accepted(accept).ShouldBeFalse();
            }

            [Fact]
            public void Ignores_a_trade_that_left_his_window_full()
            {
                var accept = Emptied with { TraderLeft = 1 };

                TradeRules.Accepted(accept).ShouldBeFalse();
            }

            [Fact]
            public void Ignores_a_trade_that_left_the_players_window_full()
            {
                var accept = Emptied with { PlayerLeft = 1 };

                TradeRules.Accepted(accept).ShouldBeFalse();
            }
        }

        public class Share
        {
            /// A client, connected, trading with a trader who has his
            /// inventory.
            static readonly TradeShareFacts Client = new TradeShareFacts
            {
                Connected = true,
                HasInventory = true,
            };

            static readonly TradeShareFacts Host = Client with
            {
                IsHost = true,
                Peers = 1,
            };

            [Fact]
            public void Sends_a_clients_trade_as_a_request()
            {
                TradeRules.Share(Client).ShouldBe(TradeSend.AsRequest);
            }

            [Fact]
            public void Sends_the_hosts_trade_to_all()
            {
                TradeRules.Share(Host).ShouldBe(TradeSend.ToAll);
            }

            [Fact]
            public void Keeps_the_hosts_trade_with_no_peers()
            {
                var trade = Host with { Peers = 0 };

                TradeRules.Share(trade).ShouldBe(TradeSend.None);
            }

            [Fact]
            public void Keeps_a_trade_with_no_connection()
            {
                var trade = Client with { Connected = false };

                TradeRules.Share(trade).ShouldBe(TradeSend.None);
            }

            [Fact]
            public void Keeps_a_trade_with_a_trader_with_no_inventory()
            {
                var trade = Host with { HasInventory = false };

                TradeRules.Share(trade).ShouldBe(TradeSend.None);
            }
        }

        public class Undo
        {
            [Fact]
            public void Gives_back_what_was_given_and_takes_what_was_taken()
            {
                var took = Items(("nail", 2));
                var gave = Items(("meat", 1));

                var change = TradeRules.Undo(took, gave);

                change.ShouldBe(Items(("nail", 2), ("meat", -1)));
            }

            [Fact]
            public void Nets_a_type_both_taken_and_given()
            {
                var took = Items(("nail", 2));
                var gave = Items(("nail", 5));

                var change = TradeRules.Undo(took, gave);

                change.ShouldBe(Items(("nail", -3)));
            }
        }

        public class UndoesStock
        {
            [Fact]
            public void Undoes_his_stock_when_no_host_stock_came()
            {
                TradeRules.UndoesStock(2, 2, true).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_his_stock_when_a_host_stock_came()
            {
                TradeRules.UndoesStock(2, 3, true).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_trader_with_no_inventory()
            {
                TradeRules.UndoesStock(2, 2, false).ShouldBeFalse();
            }
        }
    }
}
