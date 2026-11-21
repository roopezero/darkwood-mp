namespace DarkwoodMP.Hosting
{
    /// What the host box does this frame while the world loads.
    public enum HostWaitStep : byte
    {
        /// Still at the main menu within the first 20 s: the load hasn't
        /// started yet. Look again next frame.
        WaitForLoad,

        /// The world isn't ready yet. Look again next frame.
        WaitForWorld,

        /// Back at the main menu before the world was ready: give up, and log
        /// "[start game] back at the main menu before the world loaded: not
        /// hosting".
        BackAtMenu,

        /// The world is ready: go on to HostRules.Begin.
        WorldReady,
    }
}
