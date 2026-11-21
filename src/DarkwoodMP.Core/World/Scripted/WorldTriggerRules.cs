namespace DarkwoodMP.World.Scripted
{
    /// Which triggers the host's world runs for everyone, and what a
    /// trigger's coroutine carries (the plugin's
    /// ScriptedEvents.HostsWorldTrigger and EventTriggerStartPatch).
    public static class WorldTriggerRules
    {
        /// Whether this trigger, set off now, is one the host's world runs
        /// for everyone: the host sends it, a client leaves it to the
        /// host. Not inside a part being run or another event's step, not
        /// in a scene that isn't the active one, not in a client's own
        /// area; appearing only for saved, once-only objects.
        public static bool HostsWorldTrigger(in WorldTriggerFacts f)
        {
            if (!f.Matches || !f.HostWorldType)
            {
                return false;
            }

            if (!f.Active || f.Current != EventPart.All || f.StepDepth > 0)
            {
                return false;
            }

            if (!f.InActiveScene)
            {
                return false;
            }

            if (f.Client && f.LocalOnly)
            {
                return false;
            }

            if (!f.Named)
            {
                return false;
            }

            if (f.Appears && (!f.Saved || f.MultipleFire))
            {
                return false;
            }

            return true;
        }

        /// What a trigger's coroutine carries. On the host with peers, a
        /// world trigger is the world's, run whole. Otherwise one our
        /// player set off is to be shared, unless it is seen each time or
        /// goes out of sight (each player's own); the host's action
        /// applied here runs its shared part; inside a part being run, the
        /// coroutine keeps that part.
        public static Remembered Remember(in RememberFacts f)
        {
            if (f.HostWithPeers && f.HostsWorld)
            {
                return new Remembered
                {
                    Remembers = true,
                    World = true,
                    ByPlayer = true,
                    Part = EventPart.All,
                };
            }

            bool sight = f.Sight && (f.MultipleFire || f.OutOfSight);
            bool byPlayer = f.SettingOff && f.HasTriggers && !sight;
            bool all = f.Current == EventPart.All;
            bool replay = !byPlayer && all && f.SharedReplay;
            if (!byPlayer && !replay && all)
            {
                return default;
            }

            var part = f.Current;
            if (replay)
            {
                part = EventPart.SharedOnly;
            }

            return new Remembered
            {
                Remembers = true,
                ByPlayer = byPlayer,
                Part = part,
            };
        }
    }
}
