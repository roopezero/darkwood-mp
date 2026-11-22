namespace DarkwoodMP.World.Dive
{
    /// What the tank watcher knows each time it looks (SharedTank.Update).
    /// It runs with no session too.
    public readonly struct WatchFacts
    {
        /// WorldShare.InWorld.
        public bool InWorld { get; init; }

        /// WorldShare.Loading.
        public bool Loading { get; init; }

        /// The game is in its main menu (Core.mainMenu).
        public bool MainMenu { get; init; }

        /// The flag is set already (SharedTank.Filled).
        public bool Filled { get; init; }

        /// Player.Instance and its Inventory are there.
        public bool Player { get; init; }
    }
}
