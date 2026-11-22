namespace DarkwoodMP.World.Story
{
    /// What happens to a trader's after-night reward (TraderRewards).
    public enum RewardKind : byte
    {
        /// Nothing: no trader, or no amount.
        Ignored = 0,

        /// Ours, and another game's for the day came first: ours is taken
        /// back (RewardOutcome.Change is minus the amount).
        TakenBack,

        /// Ours, the first for the day: sent to the others as "ra:".
        Sent,

        /// Another game's: given here (the change can be 0) and, on the
        /// host, relayed.
        Given,

        /// Another game's at the host, and the trader got the day's
        /// already: not given, not relayed.
        Refused,
    }
}
