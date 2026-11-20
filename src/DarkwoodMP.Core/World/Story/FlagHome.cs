namespace DarkwoodMP.World.Story
{
    /// Where a flag another game sent goes (StoryRules.FlagHomeOf).
    public enum FlagHome : byte
    {
        /// The game's setFlag, once with the bool and once with the amount.
        Game = 0,

        /// One of the player's own flags: not taken.
        PlayersOwn,

        /// One of the mod's own flags (StoryKeys.ModPrefix): straight into
        /// Flags.flagsDict.
        Mod,
    }
}
