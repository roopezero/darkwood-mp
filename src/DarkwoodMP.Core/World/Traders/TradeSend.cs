namespace DarkwoodMP.World.Traders
{
    /// How our player's trade goes out (TradeRules.Share).
    public enum TradeSend
    {
        /// Not shared.
        None,

        /// The host's trade: TraderTrade to everyone.
        ToAll,

        /// A client's trade: TraderTrade to the host as a request, undone
        /// here if refused.
        AsRequest,
    }
}
