namespace DarkwoodMP.World.Fuel
{
    /// What the host last sent about a generator, and what it is now.
    public readonly struct SentFacts
    {
        /// Its level was sent before.
        public bool Sent { get; init; }

        /// The level that was sent.
        public float SentFuel { get; init; }

        /// The running state that was sent.
        public bool SentOn { get; init; }

        /// Its level now.
        public float Fuel { get; init; }

        /// Whether it runs now (Generator.isOn).
        public bool On { get; init; }
    }
}
