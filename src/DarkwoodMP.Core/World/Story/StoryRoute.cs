namespace DarkwoodMP.World.Story
{
    /// What a game does with one key another game sent (StoryRules.Route).
    public enum StoryRoute : byte
    {
        /// Applied as it is (StoryKeys.KindOf says how), and relayed.
        Apply = 0,

        /// "ra:", a trader's after-night reward (TraderRewards).
        Reward,

        /// "r:", a reputation change or a whole reputation. Always relayed.
        Reputation,

        /// "k" at the host: karma is the host's. Neither applied nor
        /// relayed.
        HostsKarma,

        /// "n:" at the host: checked against the host's NPC first
        /// (NpcStateRules.Judge).
        NpcCheck,
    }
}
