namespace DarkwoodMP.World.Rolls
{
    /// A night event that fired on the host (NightEventMessage.Fired).
    public readonly struct NightFired
    {
        /// The night scenario it is in ("" none).
        public string Scenario { get; init; }

        /// Its CustomEvent's name.
        public string Event { get; init; }

        /// The host's big location ("" none).
        public string Where { get; init; }
    }
}
