namespace DarkwoodMP.Menus
{
    /// What Start Game's Start button looks at (StartGameWindow.CantStart).
    public readonly struct StartFacts
    {
        /// The main menu is ready to start a game (WorldShare.MainMenuReady).
        public bool MenuReady { get; init; }

        /// What is chosen: a slot's number, 0 for New Game (below 0 counts
        /// as New Game too).
        public int Choice { get; init; }

        /// A slot of that number is listed and holds a save (Chosen's
        /// Profile isn't null).
        public bool ChosenUsed { get; init; }

        /// The chosen save opens in this version of the game
        /// (GameProfile.isCompatible).
        public bool ChosenCompatible { get; init; }

        /// The slot a new game goes in, 0 for none (StartGameRules.NewSlot).
        public int NewSlot { get; init; }
    }
}
