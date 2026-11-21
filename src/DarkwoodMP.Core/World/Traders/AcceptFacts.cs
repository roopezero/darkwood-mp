namespace DarkwoodMP.World.Traders
{
    /// What TraderSync.AfterTrade saw around DialogueWindow.acceptTrade. The
    /// counts are of item types (TradeRules.Count(...).Count).
    public readonly struct AcceptFacts
    {
        /// The prefix counted the windows (the window had an NPC, and
        /// Coop.Guards.Replaying wasn't set), and the window still has
        /// its NPC after.
        public bool Counted { get; init; }

        /// Types in the trader's exchange window before (what was taken).
        public int Took { get; init; }

        /// Types in the player's exchange window before (what was given).
        public int Gave { get; init; }

        /// Types left in the trader's exchange window after.
        public int TraderLeft { get; init; }

        /// Types left in the player's exchange window after.
        public int PlayerLeft { get; init; }
    }
}
