namespace DarkwoodMP.World.Story
{
    /// Where a game on its way from the ending to the main menu is
    /// (EndingRules.AfterEnding).
    public enum EndingWait : byte
    {
        /// Still on the way (the credits coming, or rolling).
        Waiting = 0,

        /// At the main menu.
        AtMenu,

        /// The credits never came: load the main menu now.
        ForceMenu,
    }
}
