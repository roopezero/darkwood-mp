namespace DarkwoodMP.World.Hideout
{
    /// A client's lit oven, when the host answered its request.
    public readonly struct RollbackFacts
    {
        /// The host allowed it.
        public bool Allowed { get; init; }

        /// The oven still exists.
        public bool Exists { get; init; }

        /// It was on before we lit it.
        public bool WasOn { get; init; }

        /// Player.Instance exists and the oven is still its own
        /// (Player.experienceMachine).
        public bool StillOurs { get; init; }
    }
}
