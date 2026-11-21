namespace DarkwoodMP.World.Hideout
{
    /// What a client does with the host's OvenLit. Zero: nothing.
    public readonly struct OvenChange
    {
        /// Light it for the world, not as our player's own.
        public bool Light { get; init; }

        /// Put the other one out, as a replay.
        public bool PutOut { get; init; }
    }
}
