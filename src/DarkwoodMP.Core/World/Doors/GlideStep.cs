namespace DarkwoodMP.World.Doors
{
    /// What LeafGlideRules.Glide decided for one leaf in one frame.
    public readonly struct GlideStep
    {
        public GlideOutcome Outcome { get; init; }

        /// Settle: the target. Turn: the angle to turn the leaf to.
        public float Angle { get; init; }
    }
}
