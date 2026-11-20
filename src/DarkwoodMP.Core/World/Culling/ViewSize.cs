namespace DarkwoodMP.World.Culling
{
    /// How far round a player the grid stays entered: Dx along x, Dy along
    /// z, in world units.
    public readonly struct ViewSize
    {
        public int Dx { get; init; }

        public int Dy { get; init; }
    }
}
