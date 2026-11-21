namespace DarkwoodMP.World.Joining
{
    /// A piece of the offered world, as a NeedSave asks for it and a
    /// SaveData's head names it: the world's hash, the offset into the
    /// bytes as sent (packed), and the length.
    public readonly struct WorldPiece
    {
        /// 32 bytes.
        public byte[] Hash { get; init; }

        public uint Offset { get; init; }

        public uint Length { get; init; }
    }
}
