namespace DarkwoodMP.Combat.Effects
{
    /// Client: the look of the host's banshees seeing our player, which
    /// the game's own code does only for its own banshees
    /// (EnemyFx.BansheeSight). The nearest one in sight sets the strength.
    public static class BansheeRules
    {
        /// The least distance the strength is worked out from.
        public const float LeastDistance = 1f;

        /// The camera shake's time, and its strength at a distance of 1.
        public const float ShakeTime = 0.5f;

        public const float ShakeAt1 = 1200f;

        /// The overlay's alpha at a distance of 1, its most, and the time
        /// it fades over.
        public const float OverlayAt1 = 70f;

        public const float MostOverlay = 0.5f;

        public const float OverlayTime = 0.5f;

        /// The agitated sound played on our player when none plays.
        public const string AgitatedSound = "banshee_agitated_player";

        /// Whether a character counts as a banshee in sight. Its light is
        /// on when it does, off when it did last time and doesn't now.
        public static bool Sees(in BansheeFacts c) =>
            c.Banshee && !c.Away && c.Alive && c.InSight;

        /// The distance used for the strength: at least LeastDistance.
        public static float Distance(float d) =>
            Floats.Max(LeastDistance, d);

        /// Which of the banshees in sight is the nearest: the first of the
        /// smallest (Distance) distances; -1 when none is under
        /// float.MaxValue, NaN included.
        public static int Nearest(float[] distances)
        {
            int nearest = -1;
            float least = float.MaxValue;
            for (int i = 0; i < distances.Length; i++)
            {
                if (distances[i] < least)
                {
                    least = distances[i];
                    nearest = i;
                }
            }

            return nearest;
        }

        /// The camera shake's strength for the nearest one's distance.
        public static float Shake(float d) => ShakeAt1 / d;

        /// The overlay's alpha for the nearest one's distance, between 0
        /// and MostOverlay.
        public static float Overlay(float d) =>
            Floats.Clamp(OverlayAt1 / d, 0f, MostOverlay);
    }
}
