namespace DarkwoodMP.Sync.Players
{
    /// One tk2d sprite of our player's own art, at the moment of the
    /// capture (PlayerCapture.Capture).
    public readonly struct SpriteFacts
    {
        /// The sprite wasn't destroyed since the last rescan.
        public bool Exists { get; init; }

        /// gameObject.activeInHierarchy.
        public bool Active { get; init; }

        /// Collection isn't null.
        public bool HasCollection { get; init; }

        /// It has a Renderer (GetComponent<Renderer>()).
        public bool HasRenderer { get; init; }

        /// Renderer.enabled.
        public bool RendererEnabled { get; init; }
    }
}
