namespace DarkwoodMP.World.Traders
{
    /// The host's answer to a TraderAsk (TraderRules.Answer).
    public enum AskAnswer
    {
        /// Nothing.
        None,

        /// His stock, to the one who asked (TraderStock).
        SendStock,

        /// Put the host's own morning trader in that joiner's hideout
        /// (TraderSync.SpawnHostCopy); his stock goes out when he stocks.
        PutCopy,
    }
}
