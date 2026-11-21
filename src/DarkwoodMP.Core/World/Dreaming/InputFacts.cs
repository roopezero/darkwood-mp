namespace DarkwoodMP.World.Dreaming
{
    /// A client's check, after a replayed dream end, that its player isn't
    /// left held (DreamSync.CheckInputs).
    public readonly struct InputFacts
    {
        /// Time.unscaledTime.
        public Seconds Now { get; init; }

        /// When the check began (DreamSync._inputsCheckFrom).
        public Seconds From { get; init; }

        /// WorldShare.InWorld.
        public bool InWorld { get; init; }

        /// Core.forbidInputs.
        public bool ForbidInputs { get; init; }

        /// DreamRules.HeldByNothing.
        public bool HeldByNothing { get; init; }
    }
}
