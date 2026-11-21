using System.Collections.Generic;

namespace DarkwoodMP.World.Traders
{
    /// Trades with a trader (the plugin's TraderSync and TraderTradePatch):
    /// which trade the player made, how it is shared, the host's check of a
    /// client's trade, and what a refused one gives back.
    public static class TradeRules
    {
        /// What a trade with no list of items took or gave.
        static readonly Dictionary<string, int> Nothing =
            new Dictionary<string, int>();

        /// Item type -> how many: each stack with a type adds its amount,
        /// and at least 1 (InventoryRules.CountOf). Empty for no list.
        /// TraderTradePatch.Count and the stock check count the same way.
        public static Dictionary<string, int> Count(
            IReadOnlyList<ItemStack> stacks)
        {
            var count = new Dictionary<string, int>();
            if (stacks == null)
            {
                return count;
            }

            for (int i = 0; i < stacks.Count; i++)
            {
                var stack = stacks[i];
                if (string.IsNullOrEmpty(stack.Type))
                {
                    continue;
                }

                count.TryGetValue(stack.Type, out int n);
                count[stack.Type] = n + InventoryRules.CountOf(stack.Amount);
            }

            return count;
        }

        /// Host: whether a client's trade may be (TraderSync.OnTrade). In
        /// this order: the trader is here; the requester is within the
        /// reach of where he stands in their game; the stock adds up
        /// (CheckStock); the reputation change can be (CheckReputation).
        /// The item values aren't recomputed: they depend on the items'
        /// state, which the host doesn't have.
        public static Verdict MayTrade(in TradeFacts trade)
        {
            if (!trade.Found)
            {
                return Verdict.Refuse(Refusal.TraderNotHere, trade.Name);
            }

            var reach = trade.Reach with
            {
                Reach = ReachRules.Reach,
                What = trade.Name,
            };

            var near = ReachRules.InReach(reach);
            if (near.Refused)
            {
                return near;
            }

            var stock = CheckStock(trade);
            if (stock.Refused)
            {
                return stock;
            }

            return CheckReputation(trade);
        }

        /// Host: whether a refused trade is answered with the trader's
        /// stock in the host's game (TraderStock to the requester), so the
        /// client's copy of him holds what the host's does. Every refusal
        /// after he was found is; "isn't here" has no stock to send.
        public static bool SendsStock(Verdict verdict)
        {
            return verdict.Refused && verdict.Reason != Refusal.TraderNotHere;
        }

        /// He must have had what was taken, and the new stock must be his
        /// stock less that, plus what was given. Every taken type is
        /// checked, in order, before any given one.
        static Verdict CheckStock(in TradeFacts trade)
        {
            var have = Count(trade.Has);
            if (trade.StockError != null)
            {
                return Verdict.Refuse(Refusal.BadStock, trade.StockError);
            }

            if (trade.Stock == null)
            {
                return Verdict.Refuse(Refusal.NoStock);
            }

            var after = Count(trade.Stock);
            foreach (var kv in Items(trade.Took))
            {
                if (kv.Value <= 0)
                {
                    var detail = Refusals.Detail(kv.Value.ToString(), kv.Key);
                    return Verdict.Refuse(Refusal.TookBadAmount, detail);
                }

                have.TryGetValue(kv.Key, out int n);
                if (n < kv.Value)
                {
                    var detail = Refusals.Detail(
                        n.ToString(),
                        kv.Value.ToString(),
                        kv.Key);
                    return Verdict.Refuse(Refusal.TraderHasLess, detail);
                }

                have[kv.Key] = n - kv.Value;
            }

            foreach (var kv in Items(trade.Gave))
            {
                if (kv.Value <= 0)
                {
                    var detail = Refusals.Detail(kv.Value.ToString(), kv.Key);
                    return Verdict.Refuse(Refusal.GaveBadAmount, detail);
                }

                have.TryGetValue(kv.Key, out int n);
                have[kv.Key] = n + kv.Value;
            }

            foreach (var kv in have)
            {
                after.TryGetValue(kv.Key, out int n);
                if (n != kv.Value)
                {
                    var detail = Refusals.Detail(
                        kv.Value.ToString(),
                        n.ToString(),
                        kv.Key);
                    return Verdict.Refuse(Refusal.StockMismatch, detail);
                }
            }

            foreach (var kv in after)
            {
                if (!have.ContainsKey(kv.Key))
                {
                    return Verdict.Refuse(Refusal.StockHasExtra, kv.Key);
                }
            }

            return Verdict.Allow;
        }

        /// As DialogueWindow.acceptTrade does, his reputation (the host's)
        /// must pay for the change; and taking only can't raise it, nor
        /// giving only lower it.
        static Verdict CheckReputation(in TradeFacts trade)
        {
            int rep = trade.Reputation;
            int change = trade.Change;
            if (rep + change < 0)
            {
                var detail = Refusals.Detail(
                    rep.ToString(),
                    (-change).ToString());
                return Verdict.Refuse(Refusal.ReputationTooLow, detail);
            }

            if (Items(trade.Gave).Count == 0 && change > 0)
            {
                return Verdict.Refuse(
                    Refusal.TakingRaisesReputation,
                    change.ToString("+0"));
            }

            if (Items(trade.Took).Count == 0 && change < 0)
            {
                return Verdict.Refuse(
                    Refusal.GivingLowersReputation,
                    change.ToString());
            }

            return Verdict.Allow;
        }

        /// The items, or none for no list (a default TradeFacts).
        static IReadOnlyDictionary<string, int> Items(
            IReadOnlyDictionary<string, int> items)
        {
            if (items == null)
            {
                return Nothing;
            }

            return items;
        }

        /// After DialogueWindow.acceptTrade (TraderTradePatch): whether
        /// the player traded. Something was in the exchange windows before,
        /// and both are empty after; a trade the game didn't accept (not
        /// enough reputation, no room) leaves the things in them.
        public static bool Accepted(in AcceptFacts accept)
        {
            if (!accept.Counted)
            {
                return false;
            }

            if (accept.Took == 0 && accept.Gave == 0)
            {
                return false;
            }

            return accept.TraderLeft == 0 && accept.PlayerLeft == 0;
        }

        /// How our player's trade is shared (TraderSync.LocalTrade): not
        /// at all with no connection, no trader inventory, or a host with
        /// no one to tell; a host's goes to all; a client's goes to the
        /// host as a request.
        public static TradeSend Share(in TradeShareFacts trade)
        {
            if (!trade.Connected || !trade.HasInventory)
            {
                return TradeSend.None;
            }

            if (trade.IsHost && trade.Peers == 0)
            {
                return TradeSend.None;
            }

            if (trade.IsHost)
            {
                return TradeSend.ToAll;
            }

            return TradeSend.AsRequest;
        }

        /// Client: what a refused trade does to its player's inventory
        /// (InventoryWatch.Undo): plus what the player took, minus what
        /// they gave. Took's types come first, in their order.
        public static Dictionary<string, int> Undo(
            IReadOnlyDictionary<string, int> took,
            IReadOnlyDictionary<string, int> gave)
        {
            var change = new Dictionary<string, int>();
            foreach (var kv in Items(took))
            {
                change[kv.Key] = kv.Value;
            }

            foreach (var kv in Items(gave))
            {
                change.TryGetValue(kv.Key, out int n);
                change[kv.Key] = n - kv.Value;
            }

            return change;
        }

        /// Client: whether a refused trade is undone in the trader's stock
        /// here too. Not when a stock from the host was applied to him
        /// since the trade (his count of applied stocks moved from seen):
        /// that one is what he has. Not when he has no inventory.
        public static bool UndoesStock(int seen, int now, bool inventory)
        {
            return now == seen && inventory;
        }
    }
}
