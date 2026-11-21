namespace DarkwoodMP.World.Building
{
    /// What a Construct message says: the construction site, and the
    /// option it was built with.
    public readonly struct BuildOrder
    {
        public ObjName Site { get; init; }

        public int Option { get; init; }
    }
}
