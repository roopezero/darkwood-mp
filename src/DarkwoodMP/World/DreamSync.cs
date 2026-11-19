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
    /// The shared dream is run by the host, as a separate location the host placed is (World.Locations.SharedDream):
    /// its enemies (the church dream's chomper) and its events are the host's, a client's own copies of them are
    /// frozen and hidden and the host's are shown, and the host's enemies there go for whoever has the most threat.
    /// Dreams that come from the story or from a kill (the night trader's) stay the player's own.
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
            s.OnSessionEnded += () =>
            {
                Shared = null;
                _hostLevelDream = false;
                _pendingStart = _pendingEnd = null;
                _inputsCheckFrom = 0f;
            };
        }

        private static bool InDream(Dreams d) => d != null && (d.dreaming || d.dreamPrepared || d.switchingDream || Core.EnteringDream);

        // ---- host -----------------------------------------------------------------------------------

        internal static void AfterConfirm(Dreams d)
        {
            if (S == null || !S.IsHost || !Hideouts.Together || d == null)
            {
                return;
            }
            if (d.wantToDream && Core.EnteringDream)
            {
                _hostLevelDream = true;
                DrawNow(d);
            }
        }

        /// <summary>
        /// The game draws the dream only after its transition has played (DreamTransition.onFinishedVideo ->
        /// Dreams.prepareDream -> getPreset), several seconds after the level. Drawing it here, as the transition
        /// starts, sends it (PresetChosen) then, so every client's transition plays at the same time as ours; the
        /// transition then goes into the dream drawn (getPreset by name doesn't draw again).
        /// </summary>
        private static void DrawNow(Dreams d)
        {
            try
            {
                string presetName = d.startTransition.dreamToTransitionTo;
                if (presetName == null)
                {
                    presetName = "";
                }
                var preset = d.getPreset(presetName);
                if (preset != null)
                {
                    d.startTransition.dreamToTransitionTo = preset.name;
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[dream] couldn't draw the level dream early, it's sent when the game draws it: {e.Message}");
            }
        }

        internal static void PresetChosen(DreamPreset preset)
        {
            if (!_hostLevelDream || Core.loadingGame)
            {
                return;
            }
            _hostLevelDream = false;
            if (preset == null || S == null || !S.IsHost || !Hideouts.Together)
            {
                return;
            }
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
            if (Shared == null || d == null || !d.dreaming || _ended || WorldActions.Replaying || S == null)
            {
                return;
            }
            _ended = true;
            string outcome = d.outcome;
            if (outcome == null)
            {
                outcome = "";
            }
            var w = EndMessage(outcome);
            if (S.IsHost)
            {
                S.Broadcast(w, Channel.Control, reliable: true);
            }
            else if (NetEnemies.ClientMode)
            {
                S.SendToHost(w, Channel.Control, reliable: true);
            }
            Plugin.Log.LogInfo($"[dream] our dream {Shared} ends ({d.outcome}): everyone's does");
        }

        private static PacketWriter EndMessage(string outcome)
        {
            var w = S.Begin(MsgType.DreamEnd);
            w.WriteStr(outcome);
            string shared = Shared;
            if (shared == null)
            {
                shared = "";
            }
            w.WriteStr(shared);
            return w;
        }

        internal static void Started()
        {
            _ended = false;
            EnteredAt = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        }

        /// <summary>For tests: when our game last started dreaming (Unix seconds; the test games share one clock).</summary>
        internal static double EnteredAt { get; private set; }

        /// <summary>The prologue's stages are dreamt together too (World.Prologue): this is the one every game is in.</summary>
        internal static void SharePrologue(string stage)
        {
            Share(stage);
            Plugin.Log.LogInfo($"[dream] the prologue's {stage}: dreamt together");
        }

        /// <summary>A dream from the story everyone dreams together (the epilogue, EpilogueSync).</summary>
        internal static void Share(string name)
        {
            Shared = name;
            _ended = false;
        }

        internal static void AfterEnd(Dreams d)
        {
            if (Shared != null && d != null && !d.dreaming && !d.switchingDream && !EpilogueSync.Playing)
            {
                Plugin.Log.LogInfo($"[dream] back from {Shared}");
                Shared = null;
            }
        }

        private static void EndHere(Dreams d, string outcome)
        {
            _ended = true;
            if (NetEnemies.ClientMode)
            {
                _inputsCheckFrom = Time.unscaledTime + InputsGrace;
            }
            d.outcome = outcome;
            WorldActions.Replaying = true;
            try
            {
                d.initiateEndDreaming();
            }
            finally
            {
                WorldActions.Replaying = false;
            }
        }

        private static void OnEnd(ulong from, PacketReader r)
        {
            string outcome = r.ReadStr();
            string dream = r.ReadStr();
            var d = Singleton<Dreams>.Instance;
            if (S.IsHost)
            {
                if (Shared == null || d == null || !d.dreaming || _ended || !EpilogueSync.OutcomeAllowed(d, outcome))
                {
                    return;
                }
                // The end of a dream we're no longer in (a prologue stage that ended here a moment before).
                if (dream != Shared)
                {
                    Plugin.Log.LogInfo($"[dream] {S.NameOf(from)}'s {dream} ended, but we're in {Shared}: not passed on");
                    return;
                }
                Plugin.Log.LogInfo($"[dream] {S.NameOf(from)}'s dream ended ({outcome}): ours and everyone's end");
                S.Broadcast(EndMessage(outcome), Channel.Control, reliable: true, except: from);
                EndHere(d, outcome);
                return;
            }
            if (from != S.HostId || Shared == null || d == null)
            {
                return;
            }
            if (!d.dreaming)
            {
                if (InDream(d) || _pendingStart != null)
                {
                    _pendingEnd = outcome;
                }
                return;
            }
            if (_ended)
            {
                return;
            }
            Plugin.Log.LogInfo($"[dream] the host's dream ended ({outcome}): ours ends");
            EndHere(d, outcome);
        }

        // ---- client ---------------------------------------------------------------------------------

        private static void OnStart(ulong from, PacketReader r)
        {
            string name = r.ReadStr();
            if (from != S.HostId || !NetEnemies.ClientMode)
            {
                return;
            }
            var d = Singleton<Dreams>.Instance;
            if (d == null)
            {
                return;
            }
            if (InDream(d))
            {
                Plugin.Log.LogInfo($"[dream] the host's level dream {name}: we're in a dream already");
                return;
            }
            _pendingStart = name;
            _pendingEnd = null;
            _startBy = Time.unscaledTime + 60f;
            _startAt = 0f;
            Plugin.Log.LogInfo($"[dream] the host's level dream {name}: we go in");
        }

        // A client: a dream ended by another game's say can end in the middle of one of our own cutscenes or
        // fades, and their end (which gives the player back) may never come then. A few seconds after the end, once
        // nothing of the game's own holds the player (no cutscene, transition, load or dialogue), let them go.
        private const float InputsGrace = 6f, InputsGiveUp = 40f;
        private static float _inputsCheckFrom;

        private static void CheckInputs(Dreams d)
        {
            if (Time.unscaledTime > _inputsCheckFrom + InputsGiveUp || !WorldShare.InWorld)
            {
                _inputsCheckFrom = 0f;
                return;
            }
            if (!Core.forbidInputs)
            {
                _inputsCheckFrom = 0f;
                return;
            }
            if (!InputsHeldByNothing(d))
            {
                return;
            }
            _inputsCheckFrom = 0f;
            Core.forbidInputs = false;
            Plugin.Log.LogInfo("[dream] the shared dream's end left our player held: let go");
        }

        /// <summary>Nothing of the game's own holds the player now (Core.forbidInputs is left over, if set).</summary>
        internal static bool InputsHeldByNothing(Dreams d)
        {
            var ctl = Singleton<Controller>.Instance;
            var o = Singleton<OutsideLocations>.Instance;
            var p = Player.Instance;
            return p != null && !Core.cantChangeForbidInputs && !Core.loadingGame && (ctl == null || !ctl.playingCutscene)
                && (o == null || !o.loading) && (d == null || !d.switchingDream) && !Core.EnteringDream && !p.inDialogue;
        }

        /// <summary>Call every frame: a client starts the host's dream once its own menus let it, and ends it if the host's already did.</summary>
        public static void Update()
        {
            var d = Singleton<Dreams>.Instance;
            if (_inputsCheckFrom > 0f && Time.unscaledTime > _inputsCheckFrom)
            {
                CheckInputs(d);
            }
            if (_pendingEnd != null && d != null && d.dreaming && !d.switchingDream)
            {
                string o = _pendingEnd;
                _pendingEnd = null;
                if (!_ended)
                {
                    Plugin.Log.LogInfo($"[dream] the host's dream had ended ({o}): ours ends");
                    EndHere(d, o);
                }
            }
            if (_pendingStart == null)
            {
                return;
            }
            var p = Player.Instance;
            if (!NetEnemies.ClientMode || !WorldShare.InWorld || d == null || p == null)
            {
                _pendingStart = null;
                return;
            }
            if (InDream(d))
            {
                _pendingStart = null;
                return;
            }
            if (Time.unscaledTime > _startBy)
            {
                Plugin.Log.LogWarning($"[dream] couldn't go into the host's dream {_pendingStart} (busy)");
                _pendingStart = null;
                return;
            }
            var skills = Singleton<SkillsMenu>.Instance;
            if ((skills != null && skills.opened) || p.inDialogue)
            {
                return; // a skill to choose, a talk to finish: then
            }
            var menu = Singleton<UI>.Instance.LevelingMenu;
            if (p.inLevelingMenu && menu != null)
            {
                if (menu.openedAndTweened)
                {
                    menu.hide(forceHide: true);
                }
                _startAt = Time.unscaledTime + 0.7f;
                return;
            }
            if (Time.unscaledTime < _startAt)
            {
                return;
            }
            string name = _pendingStart;
            _pendingStart = null;
            try
            {
                d.presetList.RemoveAll(x => x != null && x.name == name); // as the host's draw took it
                Shared = name;
                _ended = false;
                GoInto(d, name);
            }
            catch (System.Exception e)
            {
                Shared = null;
                Plugin.Log.LogError($"[dream] couldn't go into {name}: {e}");
            }
        }

        /// <summary>Into the dream through the game's transition, as SkillsMenu.confirmSkills goes into a level dream.</summary>
        private static void GoInto(Dreams d, string name)
        {
            d.wantToDream = true;
            d.startTransition.dreamToTransitionTo = name;
            Core.forbidInputs = true;
            if (Singleton<CamMain>.Instance != null)
            {
                Singleton<CamMain>.Instance.gameObject.SetActive(true);
            }
            d.startTransition.transition();
        }

        /// <summary>For tests (client): the host's dream we go into once our menus let us (null: none).</summary>
        internal static string Pending => _pendingStart;

        /// <summary>For tests (client): go into a dream of our own (as a story step's startDream does), not shared.</summary>
        internal static void TestOwnDream(string name) => GoInto(Singleton<Dreams>.Instance, name);

        /// <summary>For tests (host): start a level dream as SkillsMenu.confirmSkills does after a level.</summary>
        internal static void TestLevelDream(string name)
        {
            var d = Singleton<Dreams>.Instance;
            GoInto(d, name);
            _hostLevelDream = true;
            DrawNow(d);
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
            if (d != null && NetEnemies.ClientMode && Hideouts.Together)
            {
                __state = d.disabled;
                d.disabled = true;
            }
        }

        static void Postfix(bool __state)
        {
            var d = Singleton<Dreams>.Instance;
            if (d != null && NetEnemies.ClientMode && Hideouts.Together)
            {
                d.disabled = __state;
            }
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
