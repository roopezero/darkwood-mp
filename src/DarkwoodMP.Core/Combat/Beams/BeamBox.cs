namespace DarkwoodMP.Combat.Beams
{
    /// The box a puppet's flashlight lights the light graph in
    /// (Light2D.lightGraphNodes(Bounds), PuppetBeamLightPatch).
    public readonly struct BeamBox
    {
        /// The box replaces the game's; false leaves the game's.
        public bool Changed { get; init; }

        /// Bounds.center.
        public Vec3 Center { get; init; }

        /// Bounds.size.
        public Vec3 Size { get; init; }
    }
}
