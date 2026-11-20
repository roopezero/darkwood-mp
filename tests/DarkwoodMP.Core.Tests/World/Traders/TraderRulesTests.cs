using DarkwoodMP.World.Traders;

namespace DarkwoodMP.Tests.World.Traders
{
    public class TraderRulesTests
    {
        /// A trader called Trader, with an inventory, from no save.
        static readonly TraderCandidate Him = new TraderCandidate
        {
            Trader = true,
            Name = "Trader",
            HasInventory = true,
            Position = new Vec2(100f, 0f),
        };

        public class RollsHere
        {
            [Fact]
            public void Rolls_the_stock_in_the_hosts_game()
            {
                TraderRules.RollsHere(false).ShouldBeTrue();
            }

            [Fact]
            public void Never_rolls_the_stock_in_a_clients_game()
            {
                TraderRules.RollsHere(true).ShouldBeFalse();
            }
        }

        public class Stocking
        {
            /// A client's trader with an inventory, while connected, with
            /// no host stock waiting.
            static readonly StockFacts Client = new StockFacts
            {
                ClientMode = true,
                Trader = true,
                HasInventory = true,
                HasStock = true,
                Connected = true,
            };

            /// The host's trader, with one peer.
            static readonly StockFacts Host = new StockFacts
            {
                Trader = true,
                HasInventory = true,
                HasStock = true,
                Connected = true,
                IsHost = true,
                Peers = 1,
            };

            [Fact]
            public void Asks_the_host_on_a_client()
            {
                TraderRules.Stocking(Client).ShouldBe(StockPlan.AskHost);
            }

            [Fact]
            public void Uses_the_stock_that_came_already()
            {
                var trader = Client with { Waiting = true };

                var plan = TraderRules.Stocking(trader);

                plan.ShouldBe(StockPlan.UseWaiting);
            }

            [Fact]
            public void Does_nothing_for_a_client_npc_who_isnt_a_trader()
            {
                var trader = Client with { Trader = false, Waiting = true };

                var plan = TraderRules.Stocking(trader);

                plan.ShouldBe(StockPlan.None);
            }

            [Fact]
            public void Does_nothing_for_a_client_trader_not_come_to_life()
            {
                var trader = Client with { HasInventory = false };

                var plan = TraderRules.Stocking(trader);

                plan.ShouldBe(StockPlan.None);
            }

            [Fact]
            public void Does_nothing_on_a_client_with_no_connection()
            {
                var trader = Client with { Connected = false, Waiting = true };

                var plan = TraderRules.Stocking(trader);

                plan.ShouldBe(StockPlan.None);
            }

            [Fact]
            public void Sends_the_hosts_stock_to_all()
            {
                TraderRules.Stocking(Host).ShouldBe(StockPlan.SendToAll);
            }

            [Fact]
            public void Sends_a_component_inventory_not_come_to_life()
            {
                var trader = Host with { HasInventory = false };

                var plan = TraderRules.Stocking(trader);

                plan.ShouldBe(StockPlan.SendToAll);
            }

            [Fact]
            public void Keeps_the_hosts_stock_with_no_peers()
            {
                var trader = Host with { Peers = 0 };

                var plan = TraderRules.Stocking(trader);

                plan.ShouldBe(StockPlan.None);
            }

            [Fact]
            public void Keeps_the_stock_of_one_with_no_inventory()
            {
                var trader = Host with { HasStock = false };

                var plan = TraderRules.Stocking(trader);

                plan.ShouldBe(StockPlan.None);
            }

            [Fact]
            public void Keeps_the_stock_alone()
            {
                var trader = Host with { IsHost = false, Connected = false };

                var plan = TraderRules.Stocking(trader);

                plan.ShouldBe(StockPlan.None);
            }
        }

        public class WaitingKey
        {
            [Fact]
            public void Keeps_a_shared_save_traders_stock_by_his_ref()
            {
                var key = TraderRules.WaitingKey("Trader", true, "s:42");

                key.ShouldBe("Trader@s:42");
            }

            [Fact]
            public void Keeps_any_other_traders_stock_by_his_name()
            {
                var key = TraderRules.WaitingKey("Trader", false, "n:7");

                key.ShouldBe("Trader");
            }
        }

        public class Answer
        {
            [Fact]
            public void Sends_the_stock_of_a_trader_who_is_here()
            {
                var answer = TraderRules.Answer(true, true, true);

                answer.ShouldBe(AskAnswer.SendStock);
            }

            [Fact]
            public void Says_nothing_of_a_shared_save_trader_not_here()
            {
                var answer = TraderRules.Answer(true, false, true);

                answer.ShouldBe(AskAnswer.None);
            }

            [Fact]
            public void Puts_a_copy_of_another_trader_not_here()
            {
                var answer = TraderRules.Answer(true, false, false);

                answer.ShouldBe(AskAnswer.PutCopy);
            }

            [Fact]
            public void Ignores_an_ask_on_a_client()
            {
                var answer = TraderRules.Answer(false, true, false);

                answer.ShouldBe(AskAnswer.None);
            }
        }

        public class Find
        {
            /// A message about Trader at the origin, with a ref that names
            /// no one, and two traders of that name here.
            static readonly FindFacts Unnamed = new FindFacts
            {
                Name = "Trader",
                At = new Vec2(0f, 0f),
                Candidates = new[]
                {
                    Him with { Position = new Vec2(0f, 300f) },
                    Him,
                },
            };

            /// The same, with a valid ref that names Him here.
            static readonly FindFacts Named = Unnamed with
            {
                Valid = true,
                Resolved = true,
                ByRef = Him with { Position = new Vec2(9000f, 0f) },
            };

            [Fact]
            public void Finds_the_one_his_ref_names()
            {
                var match = TraderRules.Find(Named);

                match.How.ShouldBe(TraderFound.ByRef);
            }

            [Fact]
            public void Finds_the_nearest_of_his_name()
            {
                var match = TraderRules.Find(Unnamed);

                match.ShouldBe(Nearest(1));
            }

            [Fact]
            public void Measures_on_the_ground()
            {
                var find = Unnamed with { At = new Vec2(0f, 250f) };

                var match = TraderRules.Find(find);

                match.ShouldBe(Nearest(0));
            }

            [Fact]
            public void Takes_the_first_of_two_at_the_same_distance()
            {
                var find = Unnamed with { Candidates = new[] { Him, Him } };

                var match = TraderRules.Find(find);

                match.ShouldBe(Nearest(0));
            }

            [Fact]
            public void Finds_no_shared_save_trader_his_ref_doesnt_name()
            {
                var find = Named with { Saved = true, Resolved = false };

                var match = TraderRules.Find(find);

                match.How.ShouldBe(TraderFound.None);
            }

            [Fact]
            public void Finds_a_shared_save_trader_his_ref_names()
            {
                var find = Named with { Saved = true };

                var match = TraderRules.Find(find);

                match.How.ShouldBe(TraderFound.ByRef);
            }

            [Fact]
            public void Searches_when_the_ref_names_another_npc()
            {
                var other = Him with { Name = "Doctor" };
                var find = Named with { ByRef = other };

                var match = TraderRules.Find(find);

                match.ShouldBe(Nearest(1));
            }

            [Fact]
            public void Searches_when_the_ref_names_no_trader()
            {
                var find = Named with { ByRef = Him with { Trader = false } };

                var match = TraderRules.Find(find);

                match.ShouldBe(Nearest(1));
            }

            [Fact]
            public void Searches_when_the_ref_names_one_with_no_inventory()
            {
                var empty = Him with { HasInventory = false };
                var find = Named with { ByRef = empty };

                var match = TraderRules.Find(find);

                match.ShouldBe(Nearest(1));
            }

            [Fact]
            public void Ignores_a_resolved_npc_of_an_invalid_ref()
            {
                var find = Named with { Valid = false, Saved = true };

                var match = TraderRules.Find(find);

                match.ShouldBe(Nearest(1));
            }

            [Fact]
            public void Leaves_out_the_shared_save_traders()
            {
                var saved = Him with { Saved = true };
                var near = Him with { Position = new Vec2(0f, 300f) };
                var find = Unnamed with { Candidates = new[] { near, saved } };

                var match = TraderRules.Find(find);

                match.ShouldBe(Nearest(0));
            }

            [Theory]
            [InlineData(false, "Trader", true)]
            [InlineData(true, "Doctor", true)]
            [InlineData(true, "Trader", false)]
            public void Leaves_out_any_other_npc(
                bool trader,
                string name,
                bool inventory)
            {
                var other = new TraderCandidate
                {
                    Trader = trader,
                    Name = name,
                    HasInventory = inventory,
                };

                var find = Unnamed with { Candidates = new[] { other } };

                var match = TraderRules.Find(find);

                match.How.ShouldBe(TraderFound.None);
            }

            [Fact]
            public void Finds_no_one_with_no_list()
            {
                var find = Unnamed with { Candidates = null };

                var match = TraderRules.Find(find);

                match.How.ShouldBe(TraderFound.None);
            }

            static TraderMatch Nearest(int index) =>
                new TraderMatch { How = TraderFound.Nearest, Index = index };
        }
    }
}
