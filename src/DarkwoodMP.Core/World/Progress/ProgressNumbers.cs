using System;

namespace DarkwoodMP.World.Progress
{
    /// An essence and a level: the shared ones, or a player's own
    /// (Player.experience, Player.currentLevel).
    public readonly struct ProgressNumbers : IEquatable<ProgressNumbers>
    {
        public int Exp { get; init; }

        public int Level { get; init; }

        public bool Equals(ProgressNumbers other) =>
            Exp == other.Exp && Level == other.Level;

        public override bool Equals(object obj) =>
            obj is ProgressNumbers other && Equals(other);

        public override int GetHashCode() => Exp * 397 ^ Level;

        public static bool operator ==(ProgressNumbers a, ProgressNumbers b) =>
            a.Equals(b);

        public static bool operator !=(ProgressNumbers a, ProgressNumbers b) =>
            !a.Equals(b);
    }
}
