using System;

namespace DarkwoodMP
{
    /// A point on the ground plane: the game's x and z.
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        public readonly float X;
        public readonly float Z;

        public Vec2(float x, float z)
        {
            X = x;
            Z = z;
        }

        public static float Distance(Vec2 a, Vec2 b)
        {
            float x = a.X - b.X;
            float z = a.Z - b.Z;
            return (float)Math.Sqrt(x * x + z * z);
        }

        public bool Equals(Vec2 other) => X == other.X && Z == other.Z;

        public override bool Equals(object obj) =>
            obj is Vec2 other && Equals(other);

        public override int GetHashCode() =>
            X.GetHashCode() * 397 ^ Z.GetHashCode();

        public override string ToString() => "(" + X + ", " + Z + ")";
    }
}
