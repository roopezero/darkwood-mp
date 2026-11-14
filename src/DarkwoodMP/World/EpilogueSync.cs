using System;
using System.Collections;
using DarkwoodMP.Net;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The epilogue, played together. In Darkwood the game's last event (a startDream step into
    /// "epilog_part1a_dream", on the road home) starts a chain of dream locations: part1a, the corridor
    /// (part1b), the room (part1c), the crater, the burned forest. Each part ends with an endDream step (for example
    /// event_epilog_transportToCorridor, teleportToRoom_epilogue, HOLE_room_epilog_01_enter), whose outcome
    /// transfers to the next dream (DreamPreset.Outcome transferToDream). The outcomes screen (EpilogueOutcomes, in
    /// the last locations) then shows what became of each character from the story flags, and goToCredits loads the
    /// credits, which end at the main menu.
    ///
    /// In co-op the host's game starts the epilogue as in single player and sends GameEnd (kind 1, phase 0). The
    /// session stays up: each joiner saves its character, goes into the same epilogue dream the way the startDream
    /// step does, and the epilogue is a shared dream (DreamSync.Shared): any player's endDream moves everyone on to
    /// the same next part, and the host takes a joiner's only for an outcome the current part has. The flags are
    /// the host's (FlagsSync), so the outcomes are the same. Nobody new can join while it plays.
    ///
    /// When the host's camera pans over the burning forest (its player crawled and died, the world event
    /// epilogue_cameraPanOverBurningForest), the host sends EpilogueStep 1, and a joiner whose own pan hasn't
    /// started yet runs it a few seconds later. When the host's game goes to the credits it sends GameEnd (kind 1,
    /// phase 1) and stops the server; each joiner then goes to the credits too (if its own outcomes didn't take it
    /// there first) and leaves for the main menu. A joiner that gets to the credits first leaves the session then.
    /// </summary>
    public static class EpilogueSync
    {
        private static Session S => Session.Instance;

        public const string PanEvent = "epilogue_cameraPanOverBurningForest";
        internal enum Step : byte { Pan = 1 }

        /// <summary>The shared epilogue is on (host or joiner): from GameEnd phase 0 until the credits.</summary>
        public static bool Playing { get; private set; }
        private static bool _ownPan, _toMenu;

        public static void Register(Session s)
        {
            s.Register(MsgType.EpilogueStep, OnStep);
            s.OnSessionEnded += () => { Playing = false; _ownPan = false; };
        }

        /// <summary>Host: why a new player can't join now (null: they can).</summary>
        internal static string JoinRefusal() => Playing && S != null && S.IsHost ? "The host is playing the ending." : null;

        private static PacketWriter GameEnd(byte phase)
        {
            var w = S.Begin(MsgType.GameEnd);
            w.W.Write((byte)Chapters.EndKind.Epilogue);
            w.W.Write(phase);
            return w;
        }

        // ---- host -----------------------------------------------------------------------------------

        /// <summary>Host: our game is preparing the epilogue dream. The players in our world come along.</summary>
        internal static void HostStarts()
        {
            Playing = true;
            _ownPan = false;
            DreamSync.Share(Chapters.EpilogueDream);
            Plugin.Log.LogInfo("[epilogue] the host starts the epilogue: the players in its world play it too");
            S.Broadcast(GameEnd(0), Channel.Control, reliable: true);
            if (Chapters.TestQuick) Plugin.Instance.StartCoroutine(TestHostQuick());
        }

        /// <summary>Test: once the epilogue is on, go to the credits (as EpilogueOutcomes.goToCredits does).</summary>
        private static IEnumerator TestHostQuick()
        {
            float until = Time.realtimeSinceStartup + 60f;
            while (Time.realtimeSinceStartup < until && !(Player.Instance != null && Player.Instance.inEpilogue)) yield return null;
            Plugin.Log.LogInfo($"[epilogue] test: in the epilogue: {Player.Instance != null && Player.Instance.inEpilogue}, shared dream {DreamSync.Shared}");
            yield return new WaitForSecondsRealtime(8f);
            Plugin.Log.LogInfo("[epilogue] test: skipping to the credits");
            BeforeCredits();
            Chapters.ToCredits();
            yield return Chapters.ToMenuAfterEnding();
        }

        /// <summary>Host: a joiner's dream ended with this outcome. In the epilogue, only one its current part has.</summary>
        internal static bool OutcomeAllowed(Dreams d, string outcome)
        {
            if (!Playing || d == null || d.preset == null) return true;
            foreach (var o in d.preset.outcomes) if (o != null && o.name == outcome) return true;
            Plugin.Log.LogWarning($"[epilogue] refused a dream end: {d.preset.name} has no outcome \"{outcome}\"");
            return false;
        }

        // ---- both -----------------------------------------------------------------------------------

        /// <summary>EpilogueOutcomes.goToCredits is about to run in our game.</summary>
        internal static void BeforeCredits()
        {
            var s = S;
            if (s == null || !s.Active || !Playing) return;
            Playing = false;
            if (s.IsHost)
            {
                Plugin.Log.LogInfo("[epilogue] to the credits: the players go too, and the server stops");
                WorldShare.Instance?.StopServer(GameEnd(1));
                return;
            }
            Plugin.Log.LogInfo("[epilogue] our outcomes are over: to the credits, leaving the session");
            Leave("The game has ended.");
        }

        private static void Leave(string why)
        {
            S?.Backend?.Leave();
            WorldShare.Instance?.SetLoadStatus(why);
            if (_toMenu) return;
            _toMenu = true;
            Plugin.Instance.StartCoroutine(ToMenu());
        }

        private static IEnumerator ToMenu()
        {
            try { yield return Chapters.ToMenuAfterEnding(); }
            finally { _toMenu = false; }
        }

        /// <summary>Events.fireWorldEvent: the host's pan over the burning forest goes to the joiners.</summary>
        internal static void WorldEventFired(string type)
        {
            if (!Playing || type != PanEvent || S == null) return;
            if (WorldActions.Replaying) return;
            _ownPan = true;
            if (!S.IsHost) return;
            Plugin.Log.LogInfo("[epilogue] the camera pans over the burning forest: the joiners' too");
            var w = S.Begin(MsgType.EpilogueStep);
            w.W.Write((byte)Step.Pan);
            w.WriteStr(type);
            S.Broadcast(w, Channel.Control, reliable: true);
        }

        // ---- joiner ---------------------------------------------------------------------------------

        /// <summary>GameEnd from the host. Returns false if it isn't the shared epilogue's (Chapters handles it).</summary>
        internal static bool OnGameEnd(byte kind, byte phase)
        {
            if (kind != (byte)Chapters.EndKind.Epilogue) return false;
            if (phase == 0)
            {
                Plugin.Log.LogInfo("[epilogue] the host starts the epilogue: we go in too");
                Playing = true;
                _ownPan = false;
                Characters.Instance?.SaveNow(); // before the epilogue's own save
                Plugin.Notice("The host has reached the end of the game: the epilogue begins");
                if (WorldShare.InWorld) Plugin.Instance.StartCoroutine(JoinEpilogue());
                return true;
            }
            Plugin.Log.LogInfo("[epilogue] the host went to the credits: so do we");
            bool credits = !Playing || SceneManager.GetActiveScene().name == "credits";
            Playing = false;
            Leave("The host reached the end of the game.");
            if (!credits && (WorldShare.InWorld || (Player.Instance != null && Player.Instance.inEpilogue))) Chapters.ToCredits();
            return true;
        }

        /// <summary>Joiner: into the host's epilogue, once our game can (not mid-dream, not loading).</summary>
        private static IEnumerator JoinEpilogue()
        {
            yield return new WaitForSecondsRealtime(1f); // the notice
            float until = Time.realtimeSinceStartup + 30f;
            bool started = false;
            while (!started && Playing && Time.realtimeSinceStartup < until)
            {
                try { started = StartHere(); }
                catch (Exception e) { Plugin.Log.LogWarning($"[epilogue] couldn't start the epilogue: {e.Message}"); }
                if (!started) yield return new WaitForSecondsRealtime(1f);
            }
            if (!Playing) yield break;
            if (started)
            {
                until = Time.realtimeSinceStartup + 60f;
                while (Time.realtimeSinceStartup < until && Playing && !(Player.Instance != null && Player.Instance.inEpilogue)) yield return null;
            }
            if (Player.Instance != null && Player.Instance.inEpilogue)
            {
                Plugin.Log.LogInfo($"[epilogue] playing the epilogue together (shared dream {DreamSync.Shared})");
                yield break;
            }
            if (!Playing) yield break;
            Plugin.Log.LogWarning("[epilogue] the epilogue didn't start here: leaving, the end screen instead");
            Playing = false;
            Chapters.SoloEnding(Chapters.EndKind.EndScreen);
        }

        /// <summary>As the epilogue's startDream step does it: inputs off, a black screen, then the epilogue dream.</summary>
        private static bool StartHere()
        {
            var dreams = Singleton<Dreams>.Instance;
            var p = Player.Instance;
            if (dreams == null || p == null || dreams.dreaming || dreams.dreamPrepared || dreams.switchingDream || Core.loadingGame) return false;
            if (Singleton<OutsideLocations>.Instance != null && Singleton<OutsideLocations>.Instance.loading) return false;
            DreamSync.Share(Chapters.EpilogueDream);
            Core.forbidInputs = true;
            p.halt();
            dreams.wantToDream = true;
            Singleton<UI>.Instance.tweenBlackScreenTop(Color.black, 2f);
            Plugin.Instance.StartCoroutine(PrepareSoon(dreams));
            return true;
        }

        private static IEnumerator PrepareSoon(Dreams dreams)
        {
            yield return new WaitForSecondsRealtime(2.1f);
            var o = Singleton<OutsideLocations>.Instance;
            // The road home is where the last event runs; the game destroys it before the epilogue.
            if (o != null && o.spawnedLocations.ContainsKey("outside_roadToHome_01")) o.destroyLocation("outside_roadToHome_01");
            yield return null;
            yield return null;
            dreams.StartCoroutine(dreams.prepareDream(Chapters.EpilogueDream));
        }

        private static void OnStep(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || s.IsHost || from != s.HostId) return;
            var what = (Step)r.R.ReadByte();
            string value = r.ReadStr();
            if (what != Step.Pan || !Playing) return;
            Plugin.Log.LogInfo($"[epilogue] the host's camera panned over the burning forest{(_ownPan ? " (ours already did)" : ": ours follows")}");
            if (!_ownPan) Plugin.Instance.StartCoroutine(PanSoon(value));
        }

        private static IEnumerator PanSoon(string type)
        {
            yield return new WaitForSecondsRealtime(5f);
            if (_ownPan || !Playing || Player.Instance == null || !Player.Instance.inEpilogue) yield break;
            _ownPan = true;
            Core.forbidInputs = true;
            WorldActions.Replaying = true;
            try { Singleton<Events>.Instance.fireWorldEvent(type); }
            catch (Exception e) { Plugin.Log.LogWarning($"[epilogue] couldn't run the pan: {e.Message}"); }
            finally { WorldActions.Replaying = false; }
        }
    }

    [HarmonyPatch(typeof(EpilogueOutcomes), "goToCredits")]
    static class EpilogueCreditsPatch
    {
        static void Prefix() => EpilogueSync.BeforeCredits();
    }

    [HarmonyPatch(typeof(Events), nameof(Events.fireWorldEvent), new[] { typeof(string) })]
    static class EpilogueWorldEventPatch
    {
        static void Prefix(string type) => EpilogueSync.WorldEventFired(type);
    }
}
