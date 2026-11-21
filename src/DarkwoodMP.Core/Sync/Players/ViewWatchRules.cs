namespace DarkwoodMP.Sync.Players
{
    /// When the log says what switched off our player's view (a black
    /// screen): the game's ray down through the player's light hit
    /// something (ViewWatch.LateUpdate).
    public static class ViewWatchRules
    {
        /// Seconds (unscaled) between two looks.
        public const float Every = 0.25f;

        /// The same hiders are logged again after this long.
        public const float Repeat = 30f;

        /// Looks in a session once Every has passed. The next look is due
        /// Every after this one, even when there's no player or light to
        /// look at.
        public static bool Looks(bool session, Seconds now, Seconds next)
        {
            if (!session)
            {
                return false;
            }

            return !(now.Value < next.Value);
        }

        /// When the next look is due, after one at now.
        public static Seconds NextLook(Seconds now) =>
            new Seconds(now.Value + Every);

        /// The view isn't hidden: forget what was logged, so the next
        /// hiding is logged at once.
        public static ViewLog Clear(ViewLog old) =>
            new ViewLog { Hiders = "", At = old.At };

        /// The view is hidden by these hiders: logged when they differ
        /// from the last logged, or Repeat has passed since.
        public static bool Logs(ViewLog old, string hiders, Seconds now)
        {
            if (hiders != Hiders(old))
            {
                return true;
            }

            return !(now.Value - old.At.Value < Repeat);
        }

        /// What is remembered after logging these hiders at now.
        public static ViewLog Logged(string hiders, Seconds now) =>
            new ViewLog { Hiders = hiders, At = now };

        static string Hiders(ViewLog log)
        {
            if (log.Hiders == null)
            {
                return "";
            }

            return log.Hiders;
        }
    }
}
