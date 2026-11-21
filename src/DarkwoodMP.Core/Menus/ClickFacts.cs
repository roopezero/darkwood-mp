namespace DarkwoodMP.Menus
{
    /// A click on a server in the browser's list, and the click before it
    /// (DirectPanel.DrawBrowser).
    public readonly struct ClickFacts
    {
        /// The server clicked.
        public string Address { get; init; }

        /// The server clicked last, null before the first click
        /// (DirectPanel._lastClicked).
        public string Last { get; init; }

        /// When this click came (Time.unscaledTime).
        public Seconds Now { get; init; }

        /// When the last one did, 0 before the first
        /// (DirectPanel._lastClick).
        public Seconds LastAt { get; init; }
    }
}
