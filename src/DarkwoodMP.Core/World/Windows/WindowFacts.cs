namespace DarkwoodMP.World.Windows
{
    /// What the host knows about a window when a client asks to change
    /// it (the plugin's Windows.Allowed).
    public readonly struct WindowFacts
    {
        /// The window is in the host's world (ObjRef.Resolve of a Window).
        public bool Exists { get; init; }

        /// The requester and the window, for the reach check
        /// (Authority.InReach): Target is the window's position, What its
        /// name. The rule sets the reach itself.
        public ReachFacts Requester { get; init; }

        /// Window.barricaded.
        public bool Barricaded { get; init; }
    }
}
