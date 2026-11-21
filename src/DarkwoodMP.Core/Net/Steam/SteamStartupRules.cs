namespace DarkwoodMP.Net.Steam
{
    /// How the plugin finds Steam: Plugin.Update's check every second and
    /// Plugin.SteamReady's answer.
    public static class SteamStartupRules
    {
        /// Between two looks for Steam, in unscaled seconds.
        public const float CheckEvery = 1f;

        /// Whether to look for Steam this frame: until it's up or missing
        /// for good, once a look is due.
        public static bool CheckDue(in SteamCheckFacts check)
        {
            return !check.Up
                && !check.Missing
                && check.Now.Value >= check.Next.Value;
        }

        /// When the next look is due after one at now.
        public static Seconds NextCheck(Seconds now) =>
            new Seconds(now.Value + CheckEvery);

        /// What a look found. fields: SteamManager's s_instance and
        /// m_bInitialized fields were both found (the type is missing when
        /// they aren't). manager: s_instance holds a live object. ready: its
        /// m_bInitialized is true, read only with a manager. A build with
        /// the type but never a manager (GOG) looks forever.
        public static SteamFound Found(bool fields, bool manager, bool ready)
        {
            if (!fields)
            {
                return SteamFound.Missing;
            }

            if (manager && ready)
            {
                return SteamFound.Ready;
            }

            return SteamFound.NotYet;
        }
    }
}
