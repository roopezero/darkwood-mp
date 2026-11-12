using DarkwoodMP.Net;
using HarmonyLib;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Which save ids (SaveableObject.uniqueId) name the same object in every game.
    ///
    /// Each game hands out save ids from its own counter (SaveManager.currentUniqueID, kept in the save), to
    /// whatever the world makes while it's played: spawned characters, hiding places, delayed events, a dawn save's
    /// new objects. So once a joiner has loaded the host's world, an id from below the counter in that save means
    /// the same object in both games, and an id from above it means whatever each game made next, which differs.
    ///
    ///   Client: the counter in the save it loaded (the host's snapshot).
    ///   Host:   the counter in the snapshot each joiner was let in with. ObjRef.Of uses the lowest of them, so
    ///           every joiner has the object; Resolve accepts up to the highest, since a joiner only names ids
    ///           below its own. Both start again when the host's world changes (the joiners load it again).
    ///
    /// ObjRef names anything above the limit by name and position instead, and drops a Saved ref above it.
    /// </summary>
    public static class SharedIds
    {
        private static Session _session;
        private static int _loaded;       // the counter in the last save this game loaded
        private static int _saved;        // ... in the last save this game wrote to its own save folder
        private static int _snapshot;     // ... in the last snapshot the host wrote for a joiner
        private static int _hostMin = int.MaxValue, _hostMax; // host: over the joiners let in to this world
        private static int _hostScene;                         // ... which is this loaded scene (its handle)

        private static int Scene => UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;

        public static void Init(Session session)
        {
            _session = session;
            session.OnSessionEnded += ResetHost;
        }

        /// <summary>Save ids below this are named as Saved in refs this game sends.</summary>
        public static int SendLimit => _session != null && _session.IsHost ? (_hostMax == 0 ? 0 : _hostMin) : _loaded;

        /// <summary>Saved refs that come in with an id below this are looked up; others are dropped.</summary>
        public static int ResolveLimit => _session != null && _session.IsHost ? _hostMax : _loaded;

        /// <summary>Host: the counter in a snapshot it's about to offer, or in its own save on disk (fromSnapshot false).</summary>
        public static int CounterOf(bool fromSnapshot) => fromSnapshot ? _snapshot : _saved;

        /// <summary>Host: a joiner was let in with a world whose counter is n.</summary>
        public static void Joined(ulong peer, int n)
        {
            if (n <= 0) return;
            if (_hostScene != Scene) ResetHost(); // joiners let in to an earlier world don't count here
            if (n < _hostMin) _hostMin = n;
            if (n > _hostMax) _hostMax = n;
            Plugin.Log.LogInfo($"#{peer} joined with save ids below {n}; save ids below {_hostMin} are shared with every player");
        }

        /// <summary>Host: its world changed, so the joiners' copies are stale until they load the new one.</summary>
        public static void ResetHost() { _hostMin = int.MaxValue; _hostMax = 0; _hostScene = Scene; }

        internal static void OnLoaded(int n)
        {
            _loaded = n; _saved = n;
            Plugin.Log.LogInfo($"Loaded a world whose save id counter is at {n}");
        }

        internal static void OnSaved(int n)
        {
            if (WorldShare.Snapshotting) _snapshot = n; else _saved = n;
        }
    }

    // SaveManager.SaveState's own methods are one line each, and Mono inlines them, so a patch on them never runs.
    // The counter is read around them instead.

    /// <summary>
    /// The counter as a save writes it: SaveManager.Save gives out the new ids, then writes the counter, all before
    /// it returns.
    /// </summary>
    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.Save), new[] { typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    static class SaveIdCounterSavedPatch
    {
        static void Postfix(SaveManager __instance) => SharedIds.OnSaved(__instance.currentUniqueID);
    }

    /// <summary>
    /// The counter as a loaded save sets it: the loading coroutine sets it (SaveState.loadValues) just before it loads
    /// the player (Player.SaveState.loadValues). Characters.cs calls that too, outside loading.
    /// </summary>
    [HarmonyPatch(typeof(Player.SaveState), nameof(Player.SaveState.loadValues), new[] { typeof(bool) })]
    static class SaveIdCounterLoadedPatch
    {
        static void Prefix(bool onlyState)
        {
            var sm = Singleton<SaveManager>.Instance;
            if (!onlyState && Core.loadingGame && sm != null) SharedIds.OnLoaded(sm.currentUniqueID);
        }
    }
}
