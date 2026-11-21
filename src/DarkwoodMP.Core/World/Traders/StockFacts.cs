namespace DarkwoodMP.World.Traders
{
    /// What TraderStockPatch and TraderSync look at when a trader stocks
    /// up (NPC.randomizeTraderInv).
    public readonly struct StockFacts
    {
        /// NetEnemies.ClientMode: this game is a client's.
        public bool ClientMode { get; init; }

        /// NPC.trader.
        public bool Trader { get; init; }

        /// NPC.inventory is set (he has come to life here).
        public bool HasInventory { get; init; }

        /// He has an inventory, set or as his component (TraderSync.Inv).
        public bool HasStock { get; init; }

        /// Session.Connected.
        public bool Connected { get; init; }

        public bool IsHost { get; init; }

        /// How many peers the host has (Session.Peers.Count).
        public int Peers { get; init; }

        /// Client: the host's stock for him came already and waits
        /// (TraderSync._waiting has his WaitingKey).
        public bool Waiting { get; init; }
    }
}
