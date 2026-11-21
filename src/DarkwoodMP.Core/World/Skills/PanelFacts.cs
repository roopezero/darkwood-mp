namespace DarkwoodMP.World.Skills
{
    /// What the Skills panel looks at to open, or to close by itself.
    public readonly struct PanelFacts
    {
        /// Player.Instance is there.
        public bool HasPlayer { get; init; }

        /// WorldShare.InWorld.
        public bool InWorld { get; init; }

        /// WorldShare.Loading.
        public bool Loading { get; init; }

        /// Core.mainMenu.
        public bool MainMenu { get; init; }

        /// Core.loadingGame.
        public bool LoadingGame { get; init; }

        /// Core.Paused.
        public bool Paused { get; init; }

        /// Core.forbidInputs.
        public bool ForbidInputs { get; init; }

        /// Player.inMenu().
        public bool InMenu { get; init; }

        /// Player.dying.
        public bool Dying { get; init; }

        /// Player.health.
        public float Health { get; init; }

        /// Spectator.Active: watching another player.
        public bool Watching { get; init; }

        /// SkillTree.Tiers isn't null.
        public bool HasTiers { get; init; }

        /// The panel is open now.
        public bool Open { get; init; }
    }
}
