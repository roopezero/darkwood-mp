namespace DarkwoodMP.World.Traders
{
    /// What happens with a trader stocking up (TraderRules.Stocking).
    public enum StockPlan
    {
        /// Nothing is sent or applied.
        None,

        /// Client: apply the host's stock that came for him already.
        UseWaiting,

        /// Client: ask the host what he has (TraderAsk).
        AskHost,

        /// Host: send his new stock to everyone (TraderStock).
        SendToAll,
    }
}
