namespace DarkwoodMP.Combat.Targeting
{
    /// What the host does to an enemy's view-range list before the game's
    /// sight check.
    public enum SightStep
    {
        /// Leave the list as it is.
        Leave,

        /// A friendly NPC: ask NpcFollowers first, and unless it takes the
        /// NPC over, take every stand-in out of the list.
        FriendlyNpc,

        /// Put the players at the end of the list by threat
        /// (ThreatRules.Order).
        Reorder,
    }
}
