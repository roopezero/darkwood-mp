namespace DarkwoodMP.Sync.Players
{
    /// Our player's flashlight (PlayerCapture.Capture).
    public readonly struct BeamFacts
    {
        /// The player's "Flashlight" child is there and active (activeSelf).
        public bool On { get; init; }

        /// The child has a Light2D.
        public bool HasLight { get; init; }

        /// Light2D.LightRadius.
        public float Radius { get; init; }

        /// Light2D.LightColor.r: the beam is grey.
        public float Grey { get; init; }
    }
}
