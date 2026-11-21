namespace DarkwoodMP.Combat.Joiners
{
    /// Each client's view rect, for the host's shared culling (the
    /// plugin's ViewRects).
    public static class ViewRectRules
    {
        /// How often a client checks its rect, in unscaled seconds.
        public const float CheckEvery = 2f;

        /// The least half width or height the host keeps for a client.
        public const int MinHalf = 600;

        /// The most: a huge rect would keep the whole map running.
        public const int MaxHalf = 6000;

        /// What the host keeps of a client's rect: each half clamped to
        /// MinHalf and MaxHalf.
        public static ViewRect Accepted(ViewRect reported) =>
            new ViewRect(Clamp(reported.Dx), Clamp(reported.Dy));

        /// Whether a client may send a rect at all: neither half is 0 or
        /// less.
        public static bool Sendable(ViewRect rect) =>
            rect.Dx > 0 && rect.Dy > 0;

        /// As Mathf.Clamp on ints.
        static int Clamp(int v)
        {
            if (v < MinHalf)
            {
                return MinHalf;
            }

            if (v > MaxHalf)
            {
                return MaxHalf;
            }

            return v;
        }
    }
}
