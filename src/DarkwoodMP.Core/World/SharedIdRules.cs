namespace DarkwoodMP.World
{
    /// Which save ids are the same in every player's game (the plugin's
    /// SharedIds). A game gives later ids out itself, so only ids below
    /// the lowest counter among the players name the same object.
    public static class SharedIdRules
    {
        /// Below this a save id is sent as Saved. Host: the lowest counter
        /// among the joiners let in to this world, 0 when none was. A
        /// client, or no session: the counter of the save it loaded.
        public static int SendLimit(in IdLimitFacts ids)
        {
            if (!ids.Host)
            {
                return ids.Loaded;
            }

            if (ids.Joiners.Max == 0)
            {
                return 0;
            }

            return ids.Joiners.Min;
        }

        /// Below this a Saved name that came in is looked up. Host: the
        /// highest joiner counter (0 when none). Otherwise as SendLimit.
        public static int ResolveLimit(in IdLimitFacts ids)
        {
            if (!ids.Host)
            {
                return ids.Loaded;
            }

            return ids.Joiners.Max;
        }

        /// Host: no joiner let in to the world loaded in this scene yet.
        public static JoinerIds Reset(int scene)
        {
            return new JoinerIds { Scene = scene };
        }

        /// Host: a joiner was let in with save ids below n. A counter of 0
        /// or less is ignored; joiners of an earlier scene don't count.
        public static JoinerIds Joined(in JoinerIds was, int n, int scene)
        {
            if (n <= 0)
            {
                return was;
            }

            var ids = was;
            if (ids.Scene != scene)
            {
                ids = Reset(scene);
            }

            int min = ids.Min;
            if (ids.Max == 0 || n < ids.Min)
            {
                min = n;
            }

            int max = ids.Max;
            if (n > ids.Max)
            {
                max = n;
            }

            return ids with { Min = min, Max = max };
        }
    }
}
