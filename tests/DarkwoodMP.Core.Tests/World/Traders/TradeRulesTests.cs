using System.Collections.Generic;
using DarkwoodMP.World;
using DarkwoodMP.World.Traders;

namespace DarkwoodMP.Tests.World.Traders
{
    public partial class TradeRulesTests
    {
        static ItemStack Stack(string type, int amount) =>
            new ItemStack { Type = type, Amount = amount };

        static Dictionary<string, int> Items(params (string, int)[] items)
        {
            var count = new Dictionary<string, int>();
            foreach (var (type, n) in items)
            {
                count[type] = n;
            }

            return count;
        }

        /// A client took 2 of his 3 nails and gave him a meat, which cost 2
        /// of his 10 reputation; the client stands exactly at the reach.
        static readonly TradeFacts Fair = new TradeFacts
        {
            Name = "Trader",
            Found = true,
            Reach = new ReachFacts
            {
                Handling = true,
                Known = true,
                StandIn = new Vec2(0f, 0f),
                Target = new Vec2(600f, 0f),
            },
            Has = new[] { Stack("nail", 3), Stack("bandage", 1) },
            Stock = new[]
            {
                Stack("nail", 1),
                Stack("bandage", 1),
                Stack("meat", 1),
            },
            Took = Items(("nail", 2)),
            Gave = Items(("meat", 1)),
            Reputation = 10,
            Change = -2,
        };

        public class Count
        {
            [Fact]
            public void Adds_the_stacks_of_one_type()
            {
                var stacks = new[] { Stack("nail", 3), Stack("nail", 4) };

                var count = TradeRules.Count(stacks);

                count.ShouldBe(Items(("nail", 7)));
            }

            [Theory]
            [InlineData(0)]
            [InlineData(-5)]
            public void Counts_a_stack_of_less_than_one_as_one(int amount)
            {
                var stacks = new[] { Stack("knife", amount) };

                var count = TradeRules.Count(stacks);

                count.ShouldBe(Items(("knife", 1)));
            }

            [Theory]
            [InlineData(null)]
            [InlineData("")]
            public void Leaves_out_a_stack_with_no_type(string type)
            {
                var stacks = new[] { Stack(type, 2) };

                var count = TradeRules.Count(stacks);

                count.ShouldBeEmpty();
            }

            [Fact]
            public void Counts_nothing_for_no_list()
            {
                var count = TradeRules.Count(null);

                count.ShouldBeEmpty();
            }
        }

        public class MayTrade
        {
            [Fact]
            public void Allows_a_trade_that_adds_up()
            {
                var verdict = TradeRules.MayTrade(Fair);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_trader_who_isnt_here()
            {
                var trade = Fair with { Found = false };

                var verdict = TradeRules.MayTrade(trade);

                Refusals.Text(verdict).ShouldBe("Trader isn't here");
            }

            [Fact]
            public void Refuses_a_requester_just_past_the_reach()
            {
                var reach = Fair.Reach with { Target = new Vec2(600.5f, 0f) };
                var trade = Fair with { Reach = reach };

                var verdict = TradeRules.MayTrade(trade);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Measures_with_the_shared_reach_and_his_name()
            {
                var far = Fair.Reach with
                {
                    Target = new Vec2(4041f, 0f),
                    Reach = 9999f,
                    What = "door",
                };

                var trade = Fair with { Reach = far };

                var verdict = TradeRules.MayTrade(trade);

                Refusals.Text(verdict).ShouldBe(
                    "Trader is 4041 away from them (reach 600)");
            }

            [Fact]
            public void Allows_any_reach_outside_a_request()
            {
                var reach = Fair.Reach with
                {
                    Handling = false,
                    Target = new Vec2(4041f, 0f),
                };

                var trade = Fair with { Reach = reach };

                var verdict = TradeRules.MayTrade(trade);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_stock_that_cant_be_read()
            {
                var trade = Fair with { StockError = "Unexpected end" };

                var verdict = TradeRules.MayTrade(trade);

                Refusals.Text(verdict).ShouldBe("bad stock: Unexpected end");
            }

            [Fact]
            public void Refuses_no_stock()
            {
                var trade = Fair with { Stock = null };

                var verdict = TradeRules.MayTrade(trade);

                verdict.Reason.ShouldBe(Refusal.NoStock);
            }

            [Fact]
            public void Refuses_taking_none_of_an_item()
            {
                var trade = Fair with { Took = Items(("nail", 0)) };

                var verdict = TradeRules.MayTrade(trade);

                Refusals.Text(verdict).ShouldBe("took 0 nail");
            }

            [Fact]
            public void Refuses_taking_more_than_he_has()
            {
                var trade = Fair with { Took = Items(("nail", 4)) };

                var verdict = TradeRules.MayTrade(trade);

                Refusals.Text(verdict).ShouldBe("he has 3 nail, not 4");
            }

            [Fact]
            public void Allows_taking_exactly_what_he_has()
            {
                var trade = Fair with
                {
                    Took = Items(("nail", 3)),
                    Stock = new[] { Stack("bandage", 1), Stack("meat", 1) },
                };

                var verdict = TradeRules.MayTrade(trade);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_taking_what_he_never_had()
            {
                var trade = Fair with { Took = Items(("pipe", 1)) };

                var verdict = TradeRules.MayTrade(trade);

                Refusals.Text(verdict).ShouldBe("he has 0 pipe, not 1");
            }

            [Fact]
            public void Refuses_giving_less_than_one_of_an_item()
            {
                var trade = Fair with { Gave = Items(("meat", -1)) };

                var verdict = TradeRules.MayTrade(trade);

                Refusals.Text(verdict).ShouldBe("gave -1 meat");
            }

            [Fact]
            public void Refuses_a_stock_with_too_few_of_an_item()
            {
                var trade = Fair with
                {
                    Stock = new[] { Stack("bandage", 1), Stack("meat", 1) },
                };

                var verdict = TradeRules.MayTrade(trade);

                Refusals.Text(verdict).ShouldBe(
                    "his stock would have 1 nail, not 0");
            }

            [Fact]
            public void Refuses_a_stock_with_an_item_he_wouldnt_have()
            {
                var trade = Fair with
                {
                    Stock = new[]
                    {
                        Stack("nail", 1),
                        Stack("bandage", 1),
                        Stack("meat", 1),
                        Stack("pipe", 1),
                    },
                };

                var verdict = TradeRules.MayTrade(trade);

                Refusals.Text(verdict).ShouldBe("his stock wouldn't have pipe");
            }

            [Fact]
            public void Refuses_a_reputation_that_cant_pay()
            {
                var trade = Fair with { Reputation = 1 };

                var verdict = TradeRules.MayTrade(trade);

                Refusals.Text(verdict).ShouldBe(
                    "his reputation is 1, which doesn't pay for 2");
            }

            [Fact]
            public void Allows_a_reputation_that_pays_exactly()
            {
                var trade = Fair with { Reputation = 2 };

                var verdict = TradeRules.MayTrade(trade);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_taking_only_that_raises_the_reputation()
            {
                var trade = Fair with
                {
                    Gave = Items(),
                    Stock = new[] { Stack("nail", 1), Stack("bandage", 1) },
                    Change = 3,
                };

                var verdict = TradeRules.MayTrade(trade);

                Refusals.Text(verdict).ShouldBe(
                    "taking only can't raise the reputation (+3)");
            }

            [Fact]
            public void Allows_taking_only_that_leaves_the_reputation()
            {
                var trade = Fair with
                {
                    Gave = Items(),
                    Stock = new[] { Stack("nail", 1), Stack("bandage", 1) },
                    Change = 0,
                };

                var verdict = TradeRules.MayTrade(trade);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_giving_only_that_lowers_the_reputation()
            {
                var trade = Fair with
                {
                    Took = Items(),
                    Stock = new[]
                    {
                        Stack("nail", 3),
                        Stack("bandage", 1),
                        Stack("meat", 1),
                    },
                    Change = -1,
                };

                var verdict = TradeRules.MayTrade(trade);

                Refusals.Text(verdict).ShouldBe(
                    "giving only can't lower the reputation (-1)");
            }

            [Fact]
            public void Puts_the_trader_being_here_before_the_reach()
            {
                var reach = Fair.Reach with { Target = new Vec2(4041f, 0f) };
                var trade = Fair with { Found = false, Reach = reach };

                var verdict = TradeRules.MayTrade(trade);

                verdict.Reason.ShouldBe(Refusal.TraderNotHere);
            }

            [Fact]
            public void Puts_the_reach_before_the_stock()
            {
                var reach = Fair.Reach with { Target = new Vec2(4041f, 0f) };
                var trade = Fair with { Reach = reach, Stock = null };

                var verdict = TradeRules.MayTrade(trade);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Puts_a_bad_stock_before_a_missing_one()
            {
                var trade = Fair with { StockError = "bad", Stock = null };

                var verdict = TradeRules.MayTrade(trade);

                verdict.Reason.ShouldBe(Refusal.BadStock);
            }

            [Fact]
            public void Puts_every_taken_item_before_any_given_one()
            {
                var trade = Fair with
                {
                    Took = Items(("nail", 2), ("pipe", 1)),
                    Gave = Items(("meat", 0)),
                };

                var verdict = TradeRules.MayTrade(trade);

                verdict.Reason.ShouldBe(Refusal.TraderHasLess);
            }

            [Fact]
            public void Puts_the_stock_before_the_reputation()
            {
                var trade = Fair with
                {
                    Stock = new ItemStack[0],
                    Reputation = 0,
                };

                var verdict = TradeRules.MayTrade(trade);

                verdict.Reason.ShouldBe(Refusal.StockMismatch);
            }

            [Fact]
            public void Puts_the_cost_before_taking_only()
            {
                var trade = Fair with
                {
                    Gave = Items(),
                    Stock = new[] { Stack("nail", 1), Stack("bandage", 1) },
                    Reputation = -5,
                    Change = 3,
                };

                var verdict = TradeRules.MayTrade(trade);

                verdict.Reason.ShouldBe(Refusal.ReputationTooLow);
            }

            [Fact]
            public void Counts_a_stock_stack_of_none_as_one()
            {
                var trade = Fair with
                {
                    Stock = new[]
                    {
                        Stack("nail", 0),
                        Stack("bandage", 1),
                        Stack("meat", 1),
                    },
                };

                var verdict = TradeRules.MayTrade(trade);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_nothing_for_a_trade_of_nothing()
            {
                var trade = Fair with
                {
                    Took = Items(),
                    Gave = Items(),
                    Stock = Fair.Has,
                    Change = 0,
                };

                var verdict = TradeRules.MayTrade(trade);

                verdict.ShouldBe(Verdict.Allow);
            }
        }

        public class SendsStock
        {
            [Theory]
            [InlineData(Refusal.OutOfReach)]
            [InlineData(Refusal.NoStock)]
            [InlineData(Refusal.GivingLowersReputation)]
            public void Sends_his_stock_with_a_refusal_after_finding_him(
                Refusal reason)
            {
                var verdict = Verdict.Refuse(reason);

                TradeRules.SendsStock(verdict).ShouldBeTrue();
            }

            [Fact]
            public void Sends_nothing_for_a_trader_who_isnt_here()
            {
                var verdict = Verdict.Refuse(Refusal.TraderNotHere, "Trader");

                TradeRules.SendsStock(verdict).ShouldBeFalse();
            }

            [Fact]
            public void Sends_nothing_for_an_allowed_trade()
            {
                TradeRules.SendsStock(Verdict.Allow).ShouldBeFalse();
            }
        }
    }
}
