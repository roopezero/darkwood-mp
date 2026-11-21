namespace DarkwoodMP.World.Progress
{
    /// Our player at the stove's menu (Progression.CanResetHere).
    public readonly struct StoveFacts
    {
        /// Player.Instance is there.
        public bool HasPlayer { get; init; }

        /// Player.inLevelingMenu.
        public bool InLevelingMenu { get; init; }

        /// UI.LevelingMenu is there.
        public bool HasMenu { get; init; }

        /// LevelingMenu.openedAndTweened.
        public bool OpenedAndTweened { get; init; }

        /// LevelingMenu.wantToLevel: the bar is giving a level.
        public bool WantToLevel { get; init; }

        /// The game's SkillsMenu is there and opened.
        public bool SkillsMenuOpen { get; init; }
    }
}
