namespace DarkwoodMP.Hosting
{
    /// What the Start Game window's host box looks at each frame while the
    /// world it started loads (Plugin.HostWhenWorldReady's two loops).
    public readonly struct HostWaitFacts
    {
        /// An earlier frame's step was past WaitForLoad: the first loop is
        /// over and never runs again.
        public bool LoadStarted { get; init; }

        /// Real time since the box asked to host
        /// (Time.realtimeSinceStartup minus its value then).
        public Seconds Waited { get; init; }

        /// The main menu is up and ready (WorldShare.MainMenuReady()).
        public bool MainMenuReady { get; init; }

        /// The world is loaded and ready (WorldShare.WorldReady).
        public bool WorldReady { get; init; }
    }
}
