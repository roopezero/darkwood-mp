namespace DarkwoodMP
{
    /// Unity's Mathf.Max, Min and Clamp as the game's Mono runs them, so
    /// that a rule gives the same answer for NaN and the infinities as the
    /// shell code it copies. System.Math treats NaN otherwise.
    internal static class Floats
    {
        /// As Mathf.Max: a if it is larger, else b (NaN in b wins).
        public static float Max(float a, float b)
        {
            if (a > b)
            {
                return a;
            }

            return b;
        }

        /// As Mathf.Max for ints.
        public static int Max(int a, int b)
        {
            if (a > b)
            {
                return a;
            }

            return b;
        }

        /// As Mathf.Min: a if it is smaller, else b.
        public static int Min(int a, int b)
        {
            if (a < b)
            {
                return a;
            }

            return b;
        }

        /// As Mathf.Clamp: NaN stays NaN, as neither comparison holds.
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
    }
}
