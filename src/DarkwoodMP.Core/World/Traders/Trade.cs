using System.Collections.Generic;

namespace DarkwoodMP.World.Traders
{
    /// One trade as MsgType.TraderTrade carries it: the trader, what the
    /// player took and gave (item type -> count), the trader's stock after
    /// it (an Inventory.SaveState's JSON) and what it did to his
    /// reputation.
    public readonly struct Trade
    {
        public TraderWho Trader { get; init; }
        public IReadOnlyDictionary<string, int> Took { get; init; }
        public IReadOnlyDictionary<string, int> Gave { get; init; }
        public string Stock { get; init; }
        public int Change { get; init; }
    }
}
