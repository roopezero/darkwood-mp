namespace DarkwoodMP.World.Windows
{
    /// What a window patch looks at, after the game's call, to decide
    /// whether to send it.
    public readonly struct WindowSendFacts
    {
        /// This was the outermost patched world call
        /// (Coop.Guards.Outermost(), which the shell calls first).
        public bool Outermost { get; init; }

        /// The world is shared (Coop.Guards.Sharing).
        public bool Sharing { get; init; }

        /// The window before the call (the patch's __state).
        public WindowState Before { get; init; }

        /// The window after it.
        public WindowState After { get; init; }
    }
}
