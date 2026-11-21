namespace DarkwoodMP.World.Scripted
{
    /// A later player's part of a once-only trigger that fired for
    /// someone else (the plugin's ScriptedEvents.LaterFor and LaterPart),
    /// whose checks follow EventTrigger.fire's.
    public static class LaterRules
    {
        /// Whether this owed trigger, set off now by our player while
        /// sharing, runs as a later player's part.
        public static bool LaterFor(in LaterFacts f)
        {
            if (!f.Matches || !f.Owed)
            {
                return false;
            }

            return f.Active && f.Sharing && f.Current == EventPart.All
                && f.SettingOff;
        }

        /// After the trigger's delay: it is on, its requirements hold, it
        /// is still owed and we aren't asking for it already.
        public static bool MayStart(in LaterFacts f)
        {
            return !(f.Disabled || !f.Met || !f.Owed || f.Asking);
        }

        /// Whether an area's warm-up is waited for (and our player must
        /// still be in it after).
        public static bool WaitsWarmup(float warmup, bool area)
        {
            return warmup != 0f && area;
        }

        /// A trigger that reacts to an item taken or placed runs only for
        /// that item type, when it names one.
        public static bool ItemFits(bool itemType, string reacts, string value)
        {
            return !(itemType && !string.IsNullOrEmpty(reacts)
                && reacts != value);
        }

        /// Whether its events may run: they exist, and are active or may
        /// run inactive. Unlike FiredEventRules.CanRun, whether they ran
        /// is not looked at: being owed says they did.
        public static bool EventsRun(in EventsFacts events)
        {
            return events.Exists && (events.Active || events.RunsInactive);
        }
    }
}
