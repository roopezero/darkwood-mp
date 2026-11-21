namespace DarkwoodMP.World.Story
{
    /// What a GameEnd message says: how the game ends, and how far.
    public readonly struct EndWord
    {
        public EndKind Kind { get; init; }

        /// 0: the epilogue begins; 1: the session ends.
        public byte Phase { get; init; }
    }
}
