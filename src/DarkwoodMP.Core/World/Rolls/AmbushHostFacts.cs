namespace DarkwoodMP.World.Rolls
{
    /// The host's own player, as the day's redneck ambush looks for whom
    /// it can be (AmbushRules.HostCan).
    public readonly struct AmbushHostFacts
    {
        /// Player.Instance isn't null.
        public bool Exists { get; init; }

        /// Player.alive.
        public bool Alive { get; init; }

        /// Player.isInside.
        public bool Inside { get; init; }

        /// Player.whereAmI isn't null.
        public bool Placed { get; init; }

        /// The player is at a big location (whereAmI.bigLocation isn't
        /// null).
        public bool AtLocation { get; init; }
    }
}
