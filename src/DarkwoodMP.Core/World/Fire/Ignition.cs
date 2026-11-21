namespace DarkwoodMP.World.Fire
{
    /// The host's answer to a client's Burning: the verdict, and whether
    /// to add a Burn with this burn time. Allowed with Lights false means
    /// nothing is done.
    public readonly struct Ignition
    {
        public Verdict Verdict { get; init; }

        /// Add a Burn to the thing.
        public bool Lights { get; init; }

        /// Its burnTime.
        public float Time { get; init; }
    }
}
