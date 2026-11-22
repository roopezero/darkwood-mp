namespace DarkwoodMP.World.Dragging
{
    /// A rotation as the game's quaternion holds it (x, y, z, w), for the
    /// messages that carry one.
    public readonly struct Rotation
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;
        public readonly float W;

        public Rotation(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }
    }
}
