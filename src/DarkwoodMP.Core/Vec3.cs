namespace DarkwoodMP
{
    /// A position as it travels on the wire.
    public readonly struct Vec3
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// The game's ground plane is x-z, not x-y.
        public Vec2 Ground => new Vec2(X, Z);
    }
}
