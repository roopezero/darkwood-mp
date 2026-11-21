namespace DarkwoodMP.World.Traders
{
    /// What TraderSync.LocalTrade looks at to share our player's trade.
    public readonly struct TradeShareFacts
    {
        /// Session.Connected.
        public bool Connected { get; init; }

        /// The trader is there and has his NPC.inventory set.
        public bool HasInventory { get; init; }

        public bool IsHost { get; init; }

        /// How many peers the host has (Session.Peers.Count).
        public int Peers { get; init; }
    }
}
