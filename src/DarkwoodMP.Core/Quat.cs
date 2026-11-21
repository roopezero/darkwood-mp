namespace DarkwoodMP
{
    /// A rotation (a quaternion, x y z w) as it travels on the wire.
    public readonly struct Quat
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;
        public readonly float W;

        public Quat(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }
    }
}
