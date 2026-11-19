using System;
using System.Collections;
using System.Collections.Generic;
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
    /// The burning forest (in the crater, after the Being is set on fire): a player's first death there starts their
    /// crawl, the second ends it, and 16 s later the game pans the camera over the burning forest (the world event
    /// epilogue_cameraPanOverBurningForest), then shows the outcomes. In co-op that pan waits: a joiner's second
    /// death tells the host (EpilogueStep 3), the dead player watches the ones still crawling (Spectator.BeginWatch,
    /// not the permadeath path), and once every player has crawled to their death, or a minute after the first did,
    /// the host sends EpilogueStep 1 and every game runs its own pan once. The host's outcomes (burned forest or
    /// not) go with EpilogueStep 2, and a joiner's outcomes screen uses them. When the host's game goes to the credits it sends GameEnd (kind 1,
    /// phase 1) and stops the server; each joiner then goes to the credits too (if its own outcomes didn't take it
    /// there first) and leaves for the main menu. A joiner that gets to the credits first leaves the session then.
    /// </summary>
    public static class EpilogueSync
    {
        private static Session S => Session.Instance;

        public const string PanEvent = "epilogue_cameraPanOverBurningForest";
        internal enum Step : byte
        {
            Pan = 1,
            Outcomes = 2,
            Crawled = 3,
            Burn = 4
        }
        /// <summary>The crater's events that set the Being on fire: the burned-forest ending, once they run.</summary>
        public const string BurnEvents = "Istota_lv1_events";
        /// <summary>Seconds after the first player crawled to their death before the pan runs anyway.</summary>
        private const float PanWait = 60f;

        /// <summary>The shared epilogue is on (host or joiner): from GameEnd phase 0 until the credits.</summary>
        public static bool Playing { get; private set; }
        private static bool _ownPan, _toMenu, _panGo, _ownDeath, _burnShared;
        /// <summary>Where our player crawled to their death (the pan starts there).</summary>
        private static Vector3 _deathAt;
        /// <summary>Host: who has crawled to their death (our own LocalId too), and when the first did (-1: nobody).</summary>
        private static readonly HashSet<ulong> _crawled = new HashSet<ulong>();
        private static float _firstDeath = -1f;
        /// <summary>Joiner: the host's EpilogueOutcomes.burnedForest (-1: not heard).</summary>
        private static int _hostBurned = -1;

        public static void Register(Session s)
        {
            s.Register(MsgType.EpilogueStep, OnStep);
            s.OnPeerDisconnected += _ =>
            {
                if (Playing && S != null && S.IsHost)
                {
                    CheckPan();
                }
            };
            s.OnSessionEnded += () =>
            {
                // The host went away in the middle of the epilogue: our game plays it to the end on its own, and the
                // co-op panel opens at the main menu after the credits.
                if (Playing && S != null && S.IsClient)
                {
                    Plugin.Instance.StartCoroutine(PanelAfterCredits());
                }
                Playing = false;
                Reset();
            };
        }

        private static void Reset()
        {
            _ownPan = _panGo = _ownDeath = _burnShared = false;
            _crawled.Clear();
            _firstDeath = -1f;
            _hostBurned = -1;
            if (Spectator.Epilogue)
            {
                Spectator.EndWatch();
            }
        }

        /// <summary>A joiner whose host left mid-epilogue: once the game's own credits are over, the co-op panel.</summary>
        private static IEnumerator PanelAfterCredits()
        {
            Plugin.Log.LogInfo("[epilogue] the host left during the epilogue: we finish it alone, then the co-op panel");
            Plugin.Notice("The host left. The ending goes on in your game alone.");
            bool credits = false;
            while (true)
            {
                if (Session.Instance != null && Session.Instance.Active)
                {
                    yield break; // joined someone meanwhile
                }
                if (SceneManager.GetActiveScene().name == "credits")
                {
                    credits = true;
                }
                else if (credits && WorldShare.MainMenuReady())
                {
                    break;
                }
                yield return new WaitForSecondsRealtime(1f);
            }
            yield return new WaitForSecondsRealtime(1f);
            Plugin.Log.LogInfo("[epilogue] after the credits: the co-op panel");
            Plugin.Instance.OpenPanel();
        }

        /// <summary>
        /// Our world is in the epilogue on its own (a save made at its start, or played into it before hosting): it
        /// can't be shared then, as joiners go into it only with the host, at its start.
        /// </summary>
        internal static bool InEpilogueAlone()
        {
            if (Playing)
            {
                return false;
            }
            var p = Player.Instance;
            if (p != null && p.inEpilogue)
            {
                return true;
            }
            var d = Singleton<Dreams>.Instance;
            return d != null && (d.dreaming || d.wantToDream || d.dreamPrepared) && d.preset != null && d.preset.name != null && d.preset.name.StartsWith("epilog_");
        }

        /// <summary>Host: why a new player can't join now (null: they can).</summary>
        internal static string JoinRefusal()
        {
            if (Playing && S != null && S.IsHost)
            {
                return "The host is playing the ending.";
            }
            return null;
        }

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
            Reset();
            DreamSync.Share(Chapters.EpilogueDream);
            Plugin.Log.LogInfo("[epilogue] the host starts the epilogue: the players in its world play it too");
            S.Broadcast(GameEnd(0), Channel.Control, reliable: true);
            if (Chapters.TestQuick)
            {
                Plugin.Instance.StartCoroutine(TestHostQuick());
            }
        }

        /// <summary>Test: once the epilogue is on, go to the credits (as EpilogueOutcomes.goToCredits does).</summary>
        private static IEnumerator TestHostQuick()
        {
            float until = Time.realtimeSinceStartup + 60f;
            while (Time.realtimeSinceStartup < until && !(Player.Instance != null && Player.Instance.inEpilogue))
            {
                yield return null;
            }
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
            if (!Playing || d == null || d.preset == null)
            {
                return true;
            }
            foreach (var o in d.preset.outcomes)
            {
                if (o != null && o.name == outcome)
                {
                    return true;
                }
            }
            Plugin.Log.LogWarning($"[epilogue] refused a dream end: {d.preset.name} has no outcome \"{outcome}\"");
            return false;
        }

        // ---- both -----------------------------------------------------------------------------------

        /// <summary>EpilogueOutcomes.goToCredits is about to run in our game.</summary>
        internal static void BeforeCredits()
        {
            var s = S;
            if (s == null || !s.Active || !Playing)
            {
                return;
            }
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
            if (_toMenu)
            {
                return;
            }
            _toMenu = true;
            Plugin.Instance.StartCoroutine(ToMenu());
        }

        private static IEnumerator ToMenu()
        {
            try
            {
                yield return Chapters.ToMenuAfterEnding();
            }
            finally
            {
                _toMenu = false;
            }
        }

        /// <summary>
        /// Events.fireWorldEvent: the game's own pan over the burning forest (16 s after its player crawled to their
        /// death) waits in co-op until the host says (EpilogueStep 1). False holds it back.
        /// </summary>
        internal static bool WorldEventFired(string type)
        {
            if (!Playing || type != PanEvent || S == null || !S.Active)
            {
                return true;
            }
            if (_panGo)
            {
                if (_ownPan)
                {
                    return false; // once: ours (RunPan) or the game's own, whichever comes first
                }
                _ownPan = true;
                return true;
            }
            Plugin.Log.LogInfo("[epilogue] our pan over the burning forest waits for the others");
            return false;
        }

        /// <summary>Player.die has run: if that was the second death of the crawl in the burning forest, tell the host.</summary>
        internal static void AfterDie(Player p)
        {
            var s = S;
            if (!Playing || _ownDeath || s == null || !s.Active || p == null || !p.inEpilogue || !p.crawling || !p.dying)
            {
                return;
            }
            _ownDeath = true;
            _deathAt = p.transform.position;
            Plugin.Log.LogInfo("[epilogue] our player crawled to their death in the burning forest");
            Plugin.Instance.StartCoroutine(WatchSoon());
            if (s.IsHost)
            {
                Crawled(s.LocalId);
                return;
            }
            var w = s.Begin(MsgType.EpilogueStep);
            w.W.Write((byte)Step.Crawled);
            w.WriteStr("");
            s.SendToHost(w, Channel.Control, reliable: true);
        }

        /// <summary>After the game's own 8 s on the body, watch the players still crawling, until the pan.</summary>
        private static IEnumerator WatchSoon()
        {
            yield return new WaitForSeconds(8.5f);
            if (Playing && !_panGo && !Spectator.Active)
            {
                Spectator.BeginWatch();
            }
        }

        /// <summary>Host: this player crawled to their death.</summary>
        private static void Crawled(ulong who)
        {
            if (!_crawled.Add(who))
            {
                return;
            }
            if (_firstDeath < 0f)
            {
                _firstDeath = Time.realtimeSinceStartup;
                Plugin.Instance.StartCoroutine(PanTimeout());
            }
            CheckPan();
        }

        private static IEnumerator PanTimeout()
        {
            yield return new WaitForSecondsRealtime(PanWait);
            if (Playing && !_panGo)
            {
                Plugin.Log.LogInfo($"[epilogue] {PanWait:0} s since the first death in the burning forest: the pan runs for everyone");
                ReleasePan();
            }
        }

        /// <summary>Host: once every player has crawled to their death, the pan, in every game.</summary>
        private static void CheckPan()
        {
            var s = S;
            if (_panGo || !Playing || s == null || !s.IsHost || _firstDeath < 0f)
            {
                return;
            }
            if (!_crawled.Contains(s.LocalId))
            {
                return;
            }
            foreach (var peer in s.Peers)
            {
                if (!_crawled.Contains(peer))
                {
                    return;
                }
            }
            Plugin.Log.LogInfo("[epilogue] every player has crawled to their death: the pan runs for everyone");
            ReleasePan();
        }

        private static void ReleasePan()
        {
            var s = S;
            var w = s.Begin(MsgType.EpilogueStep);
            w.W.Write((byte)Step.Pan);
            w.WriteStr(PanEvent);
            s.Broadcast(w, Channel.Control, reliable: true);
            SendOutcomes(true); // the pan belongs to the burned forest: its outcomes follow
            RunPan();
        }

        /// <summary>
        /// The game's pan, as Player.die does it after its first 8 s: the camera drifts away from where our player
        /// died over 30 s, and 8 s in, the world event shows the burning forest and then the outcomes.
        /// </summary>
        private static void RunPan()
        {
            if (_panGo)
            {
                return;
            }
            _panGo = true;
            if (Spectator.Epilogue)
            {
                Spectator.EndWatch();
            }
            var p = Player.Instance;
            if (p == null || !p.inEpilogue)
            {
                return;
            }
            Core.forbidInputs = true;
            Vector3 from;
            if (_ownDeath)
            {
                from = _deathAt;
            }
            else
            {
                from = p.transform.position;
            }
            if (_ownDeath)
            {
                p._transform.position = from - new Vector3(0f, 10f, 0f); // where the game keeps a dead player
            }
            Plugin.Instance.StartCoroutine(Pan(from));
        }

        private static IEnumerator Pan(Vector3 from)
        {
            var follow = new GameObject("camera follow");
            follow.transform.position = from;
            var cam = Singleton<CamMain>.Instance;
            if (cam != null)
            {
                cam.followTarget = follow.transform;
            }
            float t = 0f;
            bool fired = false;
            while (t < 30f && follow != null)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / 30f);
                follow.transform.position = from - new Vector3(4000f * (0.5f - 0.5f * Mathf.Cos(k * Mathf.PI)), 0f, 0f); // ease in and out
                if (!fired && t >= 8f)
                {
                    fired = true;
                    if (!_ownPan)
                    {
                        try
                        {
                            Singleton<Events>.Instance.fireWorldEvent(PanEvent);
                        }
                        catch (Exception e)
                        {
                            Plugin.Log.LogWarning($"[epilogue] couldn't run the pan: {e.Message}");
                        }
                    }
                }
                yield return null;
            }
        }

        /// <summary>Host: our outcomes (the forest burned or not) go to the joiners, whose screens use them.</summary>
        internal static void SendOutcomes(bool burned)
        {
            var s = S;
            if (!Playing || s == null || !s.IsHost || s.Peers.Count == 0)
            {
                return;
            }
            var w = s.Begin(MsgType.EpilogueStep);
            w.W.Write((byte)Step.Outcomes);
            w.WriteStr(burned ? "1" : "0");
            s.Broadcast(w, Channel.Control, reliable: true);
        }

        /// <summary>EpilogueOutcomes.Start: the host sends its outcomes; a joiner's screen takes the host's.</summary>
        internal static void OutcomesStart(EpilogueOutcomes o)
        {
            var s = S;
            if (o == null || !Playing || s == null || !s.Active)
            {
                return;
            }
            if (s.IsHost)
            {
                SendOutcomes(o.burnedForest);
                return;
            }
            if (_hostBurned < 0 || o.burnedForest == (_hostBurned == 1))
            {
                return;
            }
            Plugin.Log.LogWarning($"[epilogue] our outcomes screen had burnedForest {o.burnedForest}, the host's {_hostBurned == 1}: using the host's");
            o.burnedForest = _hostBurned == 1;
        }

        /// <summary>
        /// Dreams.startDreaming in the epilogue: every game puts its player at the part's start, so a joiner moves
        /// to a free spot beside the others (Sync.SpawnSpot) once the part is in place.
        /// </summary>
        internal static void PartStarted()
        {
            var s = S;
            if (!Playing || s == null || !s.IsClient)
            {
                return;
            }
            Plugin.Instance.StartCoroutine(OffsetSoon());
        }

        private static IEnumerator OffsetSoon()
        {
            yield return new WaitForSecondsRealtime(0.5f);
            var p = Player.Instance;
            var sync = Sync.PlayerSync.Instance;
            if (p == null || !p.inEpilogue || sync == null)
            {
                yield break;
            }
            var taken = new List<Vector3>();
            foreach (var r in sync.Remotes.Values)
            {
                if (r.Root != null && r.Root.activeInHierarchy)
                {
                    taken.Add(r.Root.transform.position);
                }
            }
            Vector3 at = p.transform.position;
            Vector3 to = Sync.SpawnSpot.Find(p, at, taken);
            if ((to - at).sqrMagnitude < 1f)
            {
                yield break;
            }
            Plugin.Log.LogInfo($"[epilogue] the part starts where another player stands: ours moves over by {(to - at).magnitude:0}");
            p._transform.position = to;
            var body = p.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = to;
            }
        }

        // ---- joiner ---------------------------------------------------------------------------------

        /// <summary>GameEnd from the host. Returns false if it isn't the shared epilogue's (Chapters handles it).</summary>
        internal static bool OnGameEnd(byte kind, byte phase)
        {
            if (kind != (byte)Chapters.EndKind.Epilogue)
            {
                return false;
            }
            if (phase == 0)
            {
                Plugin.Log.LogInfo("[epilogue] the host starts the epilogue: we go in too");
                Playing = true;
                Reset();
                Characters.Instance?.SaveNow(); // before the epilogue's own save
                Plugin.Notice("The host has reached the end of the game: the epilogue begins");
                if (WorldShare.InWorld)
                {
                    Plugin.Instance.StartCoroutine(JoinEpilogue());
                }
                return true;
            }
            Plugin.Log.LogInfo("[epilogue] the host went to the credits: so do we");
            bool credits = !Playing || SceneManager.GetActiveScene().name == "credits";
            Playing = false;
            Leave("The host reached the end of the game.");
            if (!credits && (WorldShare.InWorld || (Player.Instance != null && Player.Instance.inEpilogue)))
            {
                Chapters.ToCredits();
            }
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
                try
                {
                    started = StartHere();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"[epilogue] couldn't start the epilogue: {e.Message}");
                }
                if (!started)
                {
                    yield return new WaitForSecondsRealtime(1f);
                }
            }
            if (!Playing)
            {
                yield break;
            }
            if (started)
            {
                until = Time.realtimeSinceStartup + 60f;
                while (Time.realtimeSinceStartup < until && Playing && !(Player.Instance != null && Player.Instance.inEpilogue))
                {
                    yield return null;
                }
            }
            if (Player.Instance != null && Player.Instance.inEpilogue)
            {
                Plugin.Log.LogInfo($"[epilogue] playing the epilogue together (shared dream {DreamSync.Shared})");
                yield break;
            }
            if (!Playing)
            {
                yield break;
            }
            Plugin.Log.LogWarning("[epilogue] the epilogue didn't start here: leaving, the end screen instead");
            Playing = false;
            Chapters.SoloEnding(Chapters.EndKind.EndScreen);
        }

        /// <summary>As the epilogue's startDream step does it: inputs off, a black screen, then the epilogue dream.</summary>
        private static bool StartHere()
        {
            var dreams = Singleton<Dreams>.Instance;
            var p = Player.Instance;
            if (dreams == null || p == null || dreams.dreaming || dreams.dreamPrepared || dreams.switchingDream || Core.loadingGame)
            {
                return false;
            }
            if (Singleton<OutsideLocations>.Instance != null && Singleton<OutsideLocations>.Instance.loading)
            {
                return false;
            }
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
            if (o != null && o.spawnedLocations.ContainsKey("outside_roadToHome_01"))
            {
                o.destroyLocation("outside_roadToHome_01");
            }
            yield return null;
            yield return null;
            dreams.StartCoroutine(dreams.prepareDream(Chapters.EpilogueDream));
        }

        private static void OnStep(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null)
            {
                return;
            }
            var what = (Step)r.R.ReadByte();
            string value = r.ReadStr();
            if (!Playing)
            {
                return;
            }
            if (s.IsHost)
            {
                if (!s.Peers.Contains(from))
                {
                    return;
                }
                if (what == Step.Burn)
                {
                    Plugin.Log.LogInfo($"[epilogue] {s.NameOf(from)} set the Being on fire");
                    Burned(from);
                    return;
                }
                if (what != Step.Crawled)
                {
                    return;
                }
                Plugin.Log.LogInfo($"[epilogue] {s.NameOf(from)} crawled to their death in the burning forest");
                Crawled(from);
                return;
            }
            if (from != s.HostId)
            {
                return;
            }
            if (what == Step.Outcomes)
            {
                if (value == "1")
                {
                    _hostBurned = 1;
                }
                else
                {
                    _hostBurned = 0;
                }
                return;
            }
            if (what == Step.Burn)
            {
                Burned(r.R.ReadUInt64());
                return;
            }
            if (what != Step.Pan)
            {
                return;
            }
            Plugin.Log.LogInfo("[epilogue] the host says the camera pans over the burning forest: ours too");
            RunPan();
        }

        // ---- the ending's choice --------------------------------------------------------------------

        /// <summary>
        /// GameEvents.fire: the crater's events that set the Being on fire ran in our game (our player burned it).
        /// That chooses the burned-forest ending for everyone: the others' games run the same events, so they're
        /// shut in the fire too. (The other ending, going back to the room and lying down to sleep, is a dream's end
        /// and moves everyone on through the shared dream.)
        /// </summary>
        internal static void EventsFired(GameEvents ge)
        {
            var s = S;
            if (!Playing || _burnShared || ge == null || ge.name != BurnEvents || WorldActions.Replaying || s == null || !s.Active)
            {
                return;
            }
            _burnShared = true;
            Plugin.Log.LogInfo("[epilogue] our player set the Being on fire: the forest burns for everyone");
            var w = s.Begin(MsgType.EpilogueStep);
            w.W.Write((byte)Step.Burn);
            w.WriteStr(BurnEvents);
            if (s.IsHost)
            {
                w.W.Write(s.LocalId);
                s.Broadcast(w, Channel.Control, reliable: true);
            }
            else
            {
                s.SendToHost(w, Channel.Control, reliable: true);
            }
        }

        /// <summary>Someone else set the Being on fire: run the same events here (the host passes it on first).</summary>
        private static void Burned(ulong who)
        {
            var s = S;
            if (_burnShared || s == null)
            {
                return;
            }
            _burnShared = true;
            if (s.IsHost)
            {
                var w = s.Begin(MsgType.EpilogueStep);
                w.W.Write((byte)Step.Burn);
                w.WriteStr(BurnEvents);
                w.W.Write(who);
                s.Broadcast(w, Channel.Control, reliable: true, except: who);
            }
            Plugin.Notice($"{s.NameOf(who)} set the Being on fire. The forest burns.");
            GameEvents found = null;
            foreach (var ge in UnityEngine.Object.FindObjectsOfType<GameEvents>(true))
            {
                if (ge != null && ge.name == BurnEvents)
                {
                    found = ge;
                    break;
                }
            }
            if (found == null)
            {
                Plugin.Log.LogWarning("[epilogue] the Being burns in another game, but our part has no such events");
                return;
            }
            if (found.fired && !found.multipleFire)
            {
                return;
            }
            Plugin.Log.LogInfo($"[epilogue] {s.NameOf(who)} set the Being on fire: running {BurnEvents} here too");
            WorldActions.Replaying = true;
            try
            {
                found.fire();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[epilogue] couldn't run {BurnEvents}: {e.Message}");
            }
            finally
            {
                WorldActions.Replaying = false;
            }
        }

        // ---- for the tests ----

        internal static bool BurnShared => _burnShared;

        internal static bool PanReleased => _panGo;
        internal static bool OwnDeath => _ownDeath;
        internal static int HostBurned => _hostBurned;
    }

    [HarmonyPatch(typeof(EpilogueOutcomes), "goToCredits")]
    static class EpilogueCreditsPatch
    {
        static void Prefix() => EpilogueSync.BeforeCredits();
    }

    [HarmonyPatch(typeof(Events), nameof(Events.fireWorldEvent), new[] { typeof(string) })]
    static class EpilogueWorldEventPatch
    {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(string type) => EpilogueSync.WorldEventFired(type);
    }

    [HarmonyPatch(typeof(GameEvents), nameof(GameEvents.fire))]
    static class EpilogueBurnPatch
    {
        static void Postfix(GameEvents __instance) => EpilogueSync.EventsFired(__instance);
    }

    [HarmonyPatch(typeof(Player), nameof(Player.die))]
    static class EpilogueDiePatch
    {
        static void Postfix(Player __instance) => EpilogueSync.AfterDie(__instance);
    }

    [HarmonyPatch(typeof(EpilogueOutcomes), "Start")]
    static class EpilogueOutcomesPatch
    {
        static void Prefix(EpilogueOutcomes __instance) => EpilogueSync.OutcomesStart(__instance);
    }

    /// <summary>The epilogue's parts: a joiner starts each one beside the others, not on top of them.</summary>
    [HarmonyPatch(typeof(Dreams), nameof(Dreams.startDreaming))]
    static class EpilogueSpawnPatch
    {
        static void Postfix() => EpilogueSync.PartStarted();
    }
}
