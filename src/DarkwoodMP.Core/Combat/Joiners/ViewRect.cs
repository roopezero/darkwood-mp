using System;

namespace DarkwoodMP.Combat.Joiners
{
    /// How far round a player their game keeps things running, as half
    /// the width and half the height of the area.
    public readonly struct ViewRect : IEquatable<ViewRect>
    {
        public readonly int Dx;
        public readonly int Dy;

        public ViewRect(int dx, int dy)
        {
            Dx = dx;
            Dy = dy;
        }

        public bool Equals(ViewRect other) =>
            Dx == other.Dx && Dy == other.Dy;

        public override bool Equals(object obj) =>
            obj is ViewRect other && Equals(other);

        public override int GetHashCode() => Dx * 397 ^ Dy;
    }
}
