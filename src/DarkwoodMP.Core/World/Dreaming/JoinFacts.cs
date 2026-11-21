namespace DarkwoodMP.World.Dreaming
{
    /// A client's pending start of the host's dream, each frame
    /// (DreamSync.Update).
    public readonly struct JoinFacts
    {
        /// NetEnemies.ClientMode.
        public bool ClientMode { get; init; }

        /// WorldShare.InWorld.
        public bool InWorld { get; init; }

        /// Singleton<Dreams>.Instance is there.
        public bool HasDreams { get; init; }

        /// Player.Instance is there.
        public bool HasPlayer { get; init; }

        /// DreamRules.InDream.
        public bool InDream { get; init; }

        /// Time.unscaledTime.
        public Seconds Now { get; init; }

        /// When it gives up (DreamSync._startBy).
        public Seconds StartBy { get; init; }

        /// Not before this (DreamSync._startAt); 0 at first.
        public Seconds StartAt { get; init; }

        /// The game's SkillsMenu is there and opened.
        public bool SkillsMenuOpen { get; init; }

        /// Player.inDialogue.
        public bool InDialogue { get; init; }

        /// Player.inLevelingMenu, with the UI's LevelingMenu there.
        public bool AtStove { get; init; }
    }
}
