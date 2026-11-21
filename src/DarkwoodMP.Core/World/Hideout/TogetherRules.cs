namespace DarkwoodMP.World.Hideout
{
    /// Playing together in a world (the plugin's Hideouts.Together), which
    /// the porter, the ovens and other subsystems ask.
    public static class TogetherRules
    {
        /// In a loaded world, not loading, hosting with peers or a client.
        /// Unlike Coop.Guards.Sharing it is true while replaying, and
        /// unlike PauseRules.InCoop it looks at Loading.
        public static bool Together(in TogetherFacts together)
        {
            if (!together.HasSession || !together.InWorld)
            {
                return false;
            }

            if (together.Loading)
            {
                return false;
            }

            if (together.IsHost && together.Peers > 0)
            {
                return true;
            }

            return together.ClientMode;
        }
    }
}
