namespace DarkwoodMP.Combat.Enemies
{
    /// A yes or no on the wire: u8 1 or 0.
    public static class Flag
    {
        public static byte Of(bool on)
        {
            if (on)
            {
                return 1;
            }

            return 0;
        }
    }
}
