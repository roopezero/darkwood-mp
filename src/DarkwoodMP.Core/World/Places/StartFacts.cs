namespace DarkwoodMP.World.Places
{
    /// The host: whether a promised location may start loading now.
    public readonly struct StartFacts
    {
        /// Names wait in the queue.
        public int Queued { get; init; }

        /// The game loads a location (OutsideLocations.loading).
        public bool Loading { get; init; }

        /// Dreams.dreaming (false with no Dreams).
        public bool Dreaming { get; init; }

        /// Dreams.dreamPrepared.
        public bool DreamPrepared { get; init; }

        /// Dreams.switchingDream.
        public bool SwitchingDream { get; init; }
    }
}
