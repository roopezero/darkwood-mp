namespace DarkwoodMP.World.Story
{
    /// What TraderRewards decided about one reward.
    public readonly struct RewardOutcome
    {
        public RewardKind Kind { get; init; }

        /// The change to make to the trader's reputation here, without
        /// sending it (FlagsSync.AddReputation, which skips 0).
        public int Change { get; init; }

        /// The amount the trader got earlier that day, for the log; 0 when
        /// none.
        public int Had { get; init; }
    }
}
