using System;

namespace DarkwoodMP.Sync.Players
{
    /// Unity's Mathf as the shell's code calls it, so that a rule rounds
    /// and clamps exactly as the game did. netstandard2.0 has no
    /// Math.Clamp, and Math.Round's default rounds halves to even, as
    /// Mathf.RoundToInt does.
    static class GameMath
    {
        /// Mathf.Clamp(float): NaN comes back as it went in.
        public static float Clamp(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }

        /// Mathf.Clamp(int).
        public static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }

        /// Mathf.Clamp01.
        public static float Clamp01(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }

            if (value > 1f)
            {
                return 1f;
            }

            return value;
        }

        /// Mathf.Max(float): "a > b ? a : b", which treats NaN unlike
        /// Math.Max.
        public static float Max(float a, float b)
        {
            if (a > b)
            {
                return a;
            }

            return b;
        }

        /// Mathf.Min(float): "a < b ? a : b".
        public static float Min(float a, float b)
        {
            if (a < b)
            {
                return a;
            }

            return b;
        }

        /// Mathf.RoundToInt: halves go to the even number.
        public static int RoundToInt(float value) =>
            (int)Math.Round((double)value);

        /// Vector3.magnitude.
        public static float Length(Vec3 v) =>
            (float)Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

        /// Vector3.sqrMagnitude.
        public static float LengthSquared(Vec3 v) =>
            v.X * v.X + v.Y * v.Y + v.Z * v.Z;
    }
}
