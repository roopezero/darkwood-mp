using System.Collections.Generic;

namespace DarkwoodMP.World
{
    /// <summary>
    /// What the trade window's two exchange windows held before
    /// DialogueWindow.acceptTrade (item type -> count), and the trader's
    /// reputation then. Took is null when nothing is to be shared.
    /// </summary>
    internal struct TradeWindows
    {
        public Dictionary<string, int> Took;
        public Dictionary<string, int> Gave;
        public int Reputation;
    }
}
