namespace DarkwoodMP.World.Culling
{
    /// The host's screen and camera (SharedCullingPatch.Distances).
    public readonly struct ScreenFacts
    {
        /// Screen.width.
        public int Width { get; init; }

        /// Screen.height.
        public int Height { get; init; }

        /// Core.ResolutionWidthModifier.
        public float WidthModifier { get; init; }

        /// Core.ResolutionHeightModifier.
        public float HeightModifier { get; init; }

        /// Controller.cameraZoom.
        public float Zoom { get; init; }
    }
}
