namespace DarkwoodMP.World.Joining
{
    /// One file a SaveOffer names: its name and unpacked size.
    public readonly struct OfferedFile
    {
        public string Name { get; init; }
        public uint Size { get; init; }
    }
}
