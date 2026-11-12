using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The level-up dreams are dreamt together. In Darkwood, taking certain levels at the stove (2, 3, 5, 6 and 7,
    /// once each: Dreams.hadDreamAtLvlN) starts a dream (SkillsMenu.confirmSkills: Dreams.startTransition; with the
    /// mod, Progression.TakeLevelAtStove does the same when the stove gives the level), the level 2 one a set dream
    /// and the others one drawn from the dreams left (Dreams.getPreset).
    ///
    /// In co-op only the host's levels bring these dreams (a client's level is taken with Dreams.disabled, so it
    /// brings no dream; progression is shared, see World.Progression). When the host's game draws the
    /// dream, the host sends its name (DreamStart), and every client goes into that same dream through the game's own
    /// transition. The dream's place is the host's (World.Locations), so the players see each other there.
    ///
    /// The dream ends together: when any player's dream ends (they died in it, or reached its end:
    /// Dreams.initiateEndDreaming), everyone's ends with that outcome (DreamEnd; a client's goes through the host).
    /// Outcomes that lead into the next dream (transferToDream) are the same in every game, so everyone goes on into
    /// the same next dream.
    ///
    /// What isn't shared: each game runs the dream's enemies and things by itself (the host's enemies don't reach
    /// into dreams, and a client's dream characters are its own, World.Locations / Combat.NetEnemies). Dreams that
    /// come from the story or from a kill (the night trader's) stay the player's own, as before.
    /// </summary>
    public static class DreamSync
    {
        private static Session S => Session.Instance;

        /// <summary>The shared dream's preset name while it runs (null: none).</summary>
        public static string Shared { get; private set; }
        private static bool _hostLevelDream; // host: our confirmSkills just started a level dream
        private static bool _ended;          // the shared dream's end was sent or replayed
        private static string _pendingStart, _pendingEnd;
        private static float _startBy, _startAt;

        public static void Register(Session s)
        {
            s.Register(MsgType.DreamStart, OnStart);
            s.Register(MsgType.DreamEnd, OnEnd);
            s.OnSessionEnded += () => { Shared = null; _hostLevelDream = false; _pendingStart = _pendingEnd = null; };
        }

        private static bool InDream(Dreams d) => d != null && (d.dreaming || d.dreamPrepared || d.switchingDream || Core.EnteringDream);

        // ---- host -----------------------------------------------------------------------------------

        internal static void AfterConfirm(Dreams d)
        {
            if (S == null || !S.IsHost || !Hideouts.Together || d == null) return;
            if (d.wantToDream && Core.EnteringDream) _hostLevelDream = true;
        }

        internal static void PresetChosen(DreamPreset preset)
        {
            if (!_hostLevelDream || preset == null || Core.loadingGame) return;
            _hostLevelDream = false;
            if (S == null || !S.IsHost || !Hideouts.Together) return;
            Shared = preset.name;
            _ended = false;
            var w = S.Begin(MsgType.DreamStart);
            w.WriteStr(preset.name);
            S.Broadcast(w, Channel.Control, reliable: true);
            Plugin.Log.LogInfo($"[dream] level dream {preset.name}: everyone goes in");
        }

        // ---- both -----------------------------------------------------------------------------------

        /// <summary>Dreams.initiateEndDreaming is about to run in our game.</summary>
        internal static void Ending(Dreams d)
        {
            if (Shared == null || d == null || !d.dreaming || _ended || WorldActions.Replaying || S == null) return;
            _ended = true;
            var w = S.Begin(MsgType.DreamEnd);
            w.WriteStr(d.outcome ?? "");
            if (S.IsHost) S.Broadcast(w, Channel.Control, reliable: true);
            else if (NetEnemies.ClientMode) S.SendToHost(w, Channel.Control, reliable: true);
            Plugin.Log.LogInfo($"[dream] our dream {Shared} ends ({d.outcome}): everyone's does");
        }

        internal static void Started() => _ended = false;

        internal static void AfterEnd(Dreams d)
        {
            if (Shared != null && d != null && !d.dreaming && !d.switchingDream) { Plugin.Log.LogInfo($"[dream] back from {Shared}"); Shared = null; }
        }

        private static void EndHere(Dreams d, string outcome)
        {
            _ended = true;
            d.outcome = outcome;
            WorldActions.Replaying = true;
            try { d.initiateEndDreaming(); }
            finally { WorldActions.Replaying = false; }
        }

        private static void OnEnd(ulong from, PacketReader r)
        {
            string outcome = r.ReadStr();
            var d = Singleton<Dreams>.Instance;
            if (S.IsHost)
            {
                if (Shared == null || d == null || !d.dreaming || _ended) return;
                Plugin.Log.LogInfo($"[dream] {S.NameOf(from)}'s dream ended ({outcome}): ours and everyone's end");
                var w = S.Begin(MsgType.DreamEnd);
                w.WriteStr(outcome);
                S.Broadcast(w, Channel.Control, reliable: true, except: from);
                EndHere(d, outcome);
                return;
            }
            if (from != S.HostId || Shared == null || d == null) return;
            if (!d.dreaming) { if (InDream(d) || _pendingStart != null) _pendingEnd = outcome; return; }
            if (_ended) return;
            Plugin.Log.LogInfo($"[dream] the host's dream ended ({outcome}): ours ends");
            EndHere(d, outcome);
        }

        // ---- client ---------------------------------------------------------------------------------

        private static void OnStart(ulong from, PacketReader r)
        {
            string name = r.ReadStr();
            if (from != S.HostId || !NetEnemies.ClientMode) return;
            var d = Singleton<Dreams>.Instance;
            if (d == null) return;
            if (InDream(d)) { Plugin.Log.LogInfo($"[dream] the host's level dream {name}: we're in a dream already"); return; }
            _pendingStart = name;
            _pendingEnd = null;
            _startBy = Time.unscaledTime + 60f;
            _startAt = 0f;
            Plugin.Log.LogInfo($"[dream] the host's level dream {name}: we go in");
        }

        /// <summary>Call every frame: a client starts the host's dream once its own menus let it, and ends it if the host's already did.</summary>
        public static void Update()
        {
            var d = Singleton<Dreams>.Instance;
            if (_pendingEnd != null && d != null && d.dreaming && !d.switchingDream)
            {
                string o = _pendingEnd; _pendingEnd = null;
                if (!_ended) { Plugin.Log.LogInfo($"[dream] the host's dream had ended ({o}): ours ends"); EndHere(d, o); }
            }
            if (_pendingStart == null) return;
            var p = Player.Instance;
            if (!NetEnemies.ClientMode || !WorldShare.InWorld || d == null || p == null) { _pendingStart = null; return; }
            if (InDream(d)) { _pendingStart = null; return; }
            if (Time.unscaledTime > _startBy) { Plugin.Log.LogWarning($"[dream] couldn't go into the host's dream {_pendingStart} (busy)"); _pendingStart = null; return; }
            var skills = Singleton<SkillsMenu>.Instance;
            if ((skills != null && skills.opened) || p.inDialogue) return; // a skill to choose, a talk to finish: then
            var menu = Singleton<UI>.Instance.LevelingMenu;
            if (p.inLevelingMenu && menu != null)
            {
                if (menu.openedAndTweened) menu.hide(forceHide: true);
                _startAt = Time.unscaledTime + 0.7f;
                return;
            }
            if (Time.unscaledTime < _startAt) return;
            string name = _pendingStart;
            _pendingStart = null;
            try
            {
                d.presetList.RemoveAll(x => x != null && x.name == name); // as the host's draw took it
                Shared = name;
                _ended = false;
                d.wantToDream = true;
                d.startTransition.dreamToTransitionTo = name;
                Core.forbidInputs = true;
                if (Singleton<CamMain>.Instance != null) Singleton<CamMain>.Instance.gameObject.SetActive(true);
                d.startTransition.transition();
            }
            catch (System.Exception e) { Shared = null; Plugin.Log.LogError($"[dream] couldn't go into {name}: {e}"); }
        }

        /// <summary>For tests: the dream we go into next (host's) or are in.</summary>
        internal static string Pending => _pendingStart;

        /// <summary>For tests (host): start a level dream as SkillsMenu.confirmSkills does after a level.</summary>
        internal static void TestLevelDream(string name)
        {
            var d = Singleton<Dreams>.Instance;
            _hostLevelDream = true;
            d.wantToDream = true;
            d.startTransition.dreamToTransitionTo = name;
            Core.forbidInputs = true;
            if (Singleton<CamMain>.Instance != null) Singleton<CamMain>.Instance.gameObject.SetActive(true);
            d.startTransition.transition();
        }
    }

    [HarmonyPatch(typeof(SkillsMenu), nameof(SkillsMenu.confirmSkills))]
    static class ConfirmSkillsPatch
    {
        static void Prefix(out bool __state)
        {
            __state = false;
            var d = Singleton<Dreams>.Instance;
            // A client's level brings no dream of its own: the host's levels bring the shared ones.
            if (d != null && NetEnemies.ClientMode && Hideouts.Together) { __state = d.disabled; d.disabled = true; }
        }

        static void Postfix(bool __state)
        {
            var d = Singleton<Dreams>.Instance;
            if (d != null && NetEnemies.ClientMode && Hideouts.Together) d.disabled = __state;
            DreamSync.AfterConfirm(d);
            Progression.LevelTaken();
        }
    }

    [HarmonyPatch(typeof(Dreams), nameof(Dreams.getPreset))]
    static class DreamPresetPatch
    {
        static void Postfix(DreamPreset __result) => DreamSync.PresetChosen(__result);
    }

    [HarmonyPatch(typeof(Dreams), nameof(Dreams.initiateEndDreaming))]
    static class DreamEndingPatch
    {
        static void Prefix(Dreams __instance) => DreamSync.Ending(__instance);
    }

    [HarmonyPatch(typeof(Dreams), nameof(Dreams.startDreaming))]
    static class DreamStartedPatch
    {
        static void Postfix() => DreamSync.Started();
    }

    [HarmonyPatch(typeof(Dreams), nameof(Dreams.endDreaming))]
    static class DreamEndedPatch
    {
        static void Postfix(Dreams __instance) => DreamSync.AfterEnd(__instance);
    }
}
