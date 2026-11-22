namespace DarkwoodMP.Net
{
    /// <summary>
    /// FNV-1a, stable across processes/platforms (string.GetHashCode is not).
    /// </summary>
    public static class StableHash
    {
        public static int Of(string s)
        {
            if (s == null)
            {
                return 0;
            }

            unchecked
            {
                uint h = 2166136261;
                for (int i = 0; i < s.Length; i++)
                {
                    h ^= s[i];
                    h *= 16777619;
                }

                return (int)h;
            }
        }
    }
}
