namespace DarkwoodMP.World.Joining
{
    /// What WorldShare's game-state reads look at.
    public readonly struct InWorldFacts
    {
        /// Core.mainMenu, and PauseMenuInWorld.
        public bool Menu { get; init; }

        public bool PauseMenu { get; init; }

        /// Core.currentProfile and Player.Instance aren't null.
        public bool Profile { get; init; }

        public bool Player { get; init; }

        /// Core.worldGenFinished(), Core.loadingGame, Core.forbidInputs.
        public bool Made { get; init; }

        public bool Loading { get; init; }

        public bool Forbid { get; init; }
    }
}
