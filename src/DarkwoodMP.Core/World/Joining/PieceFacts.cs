namespace DarkwoodMP.World.Joining
{
    /// A SaveData piece of the world being downloaded
    /// (WorldShare.OnSaveData).
    public readonly struct PieceFacts
    {
        /// Its offset and length as sent.
        public uint Offset { get; init; }

        public int Length { get; init; }

        /// The size of the world being downloaded.
        public int Total { get; init; }
    }
}
