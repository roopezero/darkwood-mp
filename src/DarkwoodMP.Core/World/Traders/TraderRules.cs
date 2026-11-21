namespace DarkwoodMP.World.Traders
{
    /// Who stocks a trader, and which trader another game's message names
    /// (the plugin's TraderSync and TraderStockPatch). A trader's stock is
    /// rolled on the host only: a client's game uses the host's.
    public static class TraderRules
    {
        /// Whether the game's own roll of a trader's stock
        /// (NPC.randomizeTraderInv) runs here: not on a client.
        public static bool RollsHere(bool clientMode)
        {
            return !clientMode;
        }

        /// What happens with a trader stocking up. On a client
        /// (TraderSync.ClientStocking), for a trader with an inventory,
        /// while connected: the host's stock that came for him already, or
        /// else ask the host. On the host, after its roll
        /// (TraderSync.HostStocked): to everyone, when it has peers and he
        /// has an inventory.
        public static StockPlan Stocking(in StockFacts trader)
        {
            if (trader.ClientMode)
            {
                if (!trader.Trader || !trader.HasInventory)
                {
                    return StockPlan.None;
                }

                if (!trader.Connected)
                {
                    return StockPlan.None;
                }

                if (trader.Waiting)
                {
                    return StockPlan.UseWaiting;
                }

                return StockPlan.AskHost;
            }

            if (!trader.IsHost || trader.Peers == 0 || !trader.HasStock)
            {
                return StockPlan.None;
            }

            return StockPlan.SendToAll;
        }

        /// What a client's stock that came for a trader not here yet is
        /// kept under: a trader from the shared save by his name and ref
        /// ("{name}@{ref}"), any other by his name alone.
        public static string WaitingKey(string name, bool saved, string obj)
        {
            if (saved)
            {
                return name + "@" + obj;
            }

            return name;
        }

        /// Host: the answer to a client's TraderAsk (TraderSync.OnAsk). His
        /// stock when he is here. A trader from the shared save who isn't
        /// here (yet) gets nothing. Any other may be the morning trader in
        /// a joiner's hideout, whom the host puts there itself
        /// (TraderCopyRules). A client ignores the message.
        public static AskAnswer Answer(bool isHost, bool found, bool saved)
        {
            if (!isHost)
            {
                return AskAnswer.None;
            }

            if (found)
            {
                return AskAnswer.SendStock;
            }

            if (saved)
            {
                return AskAnswer.None;
            }

            return AskAnswer.PutCopy;
        }

        /// Our copy of the trader another game names (TraderSync.Find): by
        /// his ref, if it names a trader of that name with an inventory
        /// here; else, unless his ref is from the shared save (whose ref
        /// names only him), the nearest trader of that name with an
        /// inventory to where the other game has him, on the ground,
        /// leaving out the shared-save traders. Of two at the same
        /// distance, the first in the list.
        public static TraderMatch Find(in FindFacts find)
        {
            if (find.Valid)
            {
                if (find.Resolved && Fits(find.ByRef, find.Name))
                {
                    return new TraderMatch { How = TraderFound.ByRef };
                }

                if (find.Saved)
                {
                    return default;
                }
            }

            var list = find.Candidates;
            if (list == null)
            {
                return default;
            }

            int best = -1;
            float bestSquared = float.MaxValue;
            for (int i = 0; i < list.Count; i++)
            {
                var trader = list[i];
                if (!Fits(trader, find.Name) || trader.Saved)
                {
                    continue;
                }

                float d = FlatSquared(trader.Position, find.At);
                if (d < bestSquared)
                {
                    bestSquared = d;
                    best = i;
                }
            }

            if (best < 0)
            {
                return default;
            }

            return new TraderMatch { How = TraderFound.Nearest, Index = best };
        }

        /// A trader of this name, with an inventory.
        static bool Fits(in TraderCandidate trader, string name)
        {
            return trader.Trader && trader.Name == name && trader.HasInventory;
        }

        /// As WorldActions.FlatSq: the squared distance on the ground.
        internal static float FlatSquared(Vec2 a, Vec2 b)
        {
            return (a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z);
        }
    }
}
