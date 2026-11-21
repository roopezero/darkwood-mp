namespace DarkwoodMP.Combat.Motion
{
    /// A rotation as the host sends it (a Unity quaternion's x, y, z and
    /// w). The core only carries it; the shell turns between two with
    /// Quaternion.Slerp.
    public readonly struct Facing
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;
        public readonly float W;

        public Facing(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        /// Quaternion.identity: what Where gives when it has no state.
        public static Facing Identity => new Facing(0f, 0f, 0f, 1f);
    }
}
