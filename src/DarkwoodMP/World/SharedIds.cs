using HarmonyLib;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Which save ids (SaveableObject.uniqueId) name the same object in every
    /// game.
    ///
    /// Each game hands out save ids from its own counter
    /// (SaveManager.currentUniqueID, kept in the save), to whatever the world
    /// makes while it's played: spawned characters, hiding places, delayed
    /// events, a dawn save's new objects. So once a joiner has loaded the
    /// host's world, an id from below the counter in that save means the same
    /// object in both games, and an id from above it means whatever each game
    /// made next, which differs.
    ///
    ///   Client: the counter in the save it loaded (the host's snapshot).
    ///   Host:   the counter in the snapshot each joiner was let in with.
    ///           ObjRef.Of uses the lowest of them, so every joiner has the
    ///           object; Resolve accepts up to the highest, since a joiner only
    ///           names ids below its own. Both start again when the host's
    ///           world changes (the joiners load it again).
    ///
    /// ObjRef names anything above the limit by name and position instead, and
    /// drops a Saved ref above it.
    /// </summary>
    public static class SharedIds
    {
        /// <summary>
        /// Save ids below this are named as Saved in refs this game sends.
        /// </summary>
        public static int SendLimit => SharedIdRules.SendLimit(FactsOf());

        /// <summary>
        /// Saved refs that come in with an id below this are looked up; others
        /// are dropped.
        /// </summary>
        public static int ResolveLimit =>
            SharedIdRules.ResolveLimit(FactsOf());

        // The save counters live as long as the game (ModHost.SaveIds), the
        // joiners' as long as the session (CoopSession.Joiners).
        private static SaveIdCounters Counters => Plugin.Instance.Host.SaveIds;

        private static IdLimitFacts FactsOf()
        {
            CoopSession coop = Coop.Current;
            var facts = new IdLimitFacts { Loaded = Counters.Loaded };
            if (coop == null)
            {
                return facts;
            }

            return facts with
            {
                Host = coop.Net.IsHost,
                Joiners = coop.Joiners.Ids,
            };
        }

        /// <summary>
        /// Host: the counter in a snapshot it's about to offer, or in its own
        /// save on disk (fromSnapshot false).
        /// </summary>
        public static int CounterOf(bool fromSnapshot)
        {
            return Counters.CounterOf(fromSnapshot);
        }

        /// <summary>
        /// Host: a joiner was let in with a world whose counter is n.
        /// </summary>
        public static void Joined(ulong peer, int n)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Joiners.Joined(peer, n);
            }
        }

        /// <summary>
        /// Host: its world changed, so the joiners' copies are stale until they
        /// load the new one.
        /// </summary>
        public static void ResetHost()
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Joiners.Reset();
            }
        }

        internal static void OnLoaded(int n)
        {
            Counters.OnLoaded(n);
        }

        internal static void OnSaved(int n)
        {
            Counters.OnSaved(n);
        }
    }

    // SaveManager.SaveState's own methods are one line each, and Mono inlines
    // them, so a patch on them never runs. The counter is read around them
    // instead.

    /// <summary>
    /// The counter as a save writes it: SaveManager.Save gives out the new ids,
    /// then writes the counter, all before it returns.
    /// </summary>
    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.Save), new[]
    {
        typeof(bool),
        typeof(bool),
        typeof(bool),
        typeof(bool),
        typeof(bool),
        typeof(bool),
        typeof(bool)
    })]
    static class SaveIdCounterSavedPatch
    {
        static void Postfix(SaveManager __instance) =>
            SharedIds.OnSaved(__instance.currentUniqueID);
    }

    /// <summary>
    /// The counter as a loaded save sets it: the loading coroutine sets it
    /// (SaveState.loadValues) just before it loads the player
    /// (Player.SaveState.loadValues). Characters.cs calls that too, outside
    /// loading.
    /// </summary>
    [HarmonyPatch(
        typeof(Player.SaveState), nameof(Player.SaveState.loadValues),
        new[] { typeof(bool) })]
    static class SaveIdCounterLoadedPatch
    {
        static void Prefix(bool onlyState)
        {
            var sm = Singleton<SaveManager>.Instance;
            if (!onlyState && Core.loadingGame && sm != null)
            {
                SharedIds.OnLoaded(sm.currentUniqueID);
            }
        }
    }
}
