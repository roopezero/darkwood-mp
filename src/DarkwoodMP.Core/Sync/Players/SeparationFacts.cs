namespace DarkwoodMP.Sync.Players
{
    /// Whether our player is eased out of the others this frame
    /// (PlayerSeparation.Update).
    public readonly struct SeparationFacts
    {
        /// The SoftPlayerCollision setting is there and off. With no
        /// config, or no such entry, it counts as on.
        public bool SwitchedOff { get; init; }

        /// WorldShare.InWorld.
        public bool InWorld { get; init; }

        /// PlayerSync.Remotes.Count, the dummy included.
        public int Puppets { get; init; }

        /// Player.Instance isn't null.
        public bool HasPlayer { get; init; }

        /// Player.alive.
        public bool Alive { get; init; }

        /// Player.dying.
        public bool Dying { get; init; }

        /// Core.forbidInputs.
        public bool ForbidInputs { get; init; }

        /// The player has a Rigidbody.
        public bool HasBody { get; init; }

        /// Rigidbody.isKinematic.
        public bool Kinematic { get; init; }
    }
}
