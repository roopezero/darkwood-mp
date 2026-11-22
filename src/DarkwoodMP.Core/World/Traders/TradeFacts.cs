using System.Collections.Generic;

namespace DarkwoodMP.World.Traders
{
    /// What the host knows about a client's trade (TraderTrade, as a
    /// request) when it checks it (TraderSync.OnTrade).
    public readonly struct TradeFacts
    {
        /// The trader's name, as the client sent it.
        public string Name { get; init; }

        /// TraderRules.Find found the host's copy of him.
        public bool Found { get; init; }

        /// The requester and where the trader stands in the requester's
        /// game (the message's x and z, not the host's copy). The rule
        /// sets the reach (ReachRules.Reach) and the name itself.
        public ReachFacts Reach { get; init; }

        /// What the host's copy of him holds now (Inv(npc).getAllItems()).
        public IReadOnlyList<ItemStack> Has { get; init; }

        /// The message of the error the client's stock JSON gave when read,
        /// or null when it was read.
        public string StockError { get; init; }

        /// The stock's items (SaveState.invItems); null when the stock or
        /// its list is missing.
        public IReadOnlyList<ItemStack> Stock { get; init; }

        /// Item type -> count the client's player took from him.
        public IReadOnlyDictionary<string, int> Took { get; init; }

        /// Item type -> count the client's player gave him.
        public IReadOnlyDictionary<string, int> Gave { get; init; }

        /// His reputation in the host's game (Flags.getNPCState(name)),
        /// 0 when he has no state there.
        public int Reputation { get; init; }

        /// What the trade did to his reputation, as the client sent it.
        public int Change { get; init; }
    }
}
