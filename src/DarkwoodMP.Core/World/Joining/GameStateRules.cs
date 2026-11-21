namespace DarkwoodMP.World.Joining
{
    /// Whether the game is in a world, and whether that world is ready
    /// (WorldShare.InWorld, PauseMenuInWorld, WorldReady), read by every
    /// subsystem.
    public static class GameStateRules
    {
        /// A world counts as ready this long after it first looked ready.
        public static readonly Seconds ReadyDelay = new Seconds(2f);

        /// The pause menu over a world (PauseMenuInWorld): Core.mainMenu
        /// with a WorldGenerator whose world is made.
        public static bool PauseMenuInWorld(
            bool menu,
            bool generator,
            bool made) =>
            menu && generator && made;

        /// In a world (InWorld): no menu, or the pause menu over a world,
        /// with a current profile and a Player.
        public static bool InWorld(in InWorldFacts facts) =>
            (!facts.Menu || facts.PauseMenu)
                && facts.Profile
                && facts.Player;

        /// The world looks ready now: in it, made, not loading, inputs not
        /// forbidden.
        public static bool LooksReady(in InWorldFacts facts) =>
            InWorld(facts) && facts.Made && !facts.Loading && !facts.Forbid;

        /// The WorldReady latch: since is when it first looked ready (below
        /// zero: not), and it is ready more than ReadyDelay after. A read
        /// that sees it not ready starts it over.
        public static ReadyStep Ready(bool looks, Seconds since, Seconds now)
        {
            if (!looks)
            {
                return new ReadyStep { Since = new Seconds(-1f) };
            }

            if (since.Value < 0f)
            {
                since = now;
            }

            return new ReadyStep
            {
                Ready = now - since > ReadyDelay,
                Since = since,
            };
        }
    }
}
