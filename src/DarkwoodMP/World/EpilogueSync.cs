using DarkwoodMP.World.Dreaming;
using DarkwoodMP.World.Story;
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
    /// The epilogue, played together. In Darkwood the game's last event (a
    /// startDream step into "epilog_part1a_dream", on the road home) starts a
    /// chain of dream locations: part1a, the corridor (part1b), the room
    /// (part1c), the crater, the burned forest. Each part ends with an endDream
    /// step (for example event_epilog_transportToCorridor,
    /// teleportToRoom_epilogue, HOLE_room_epilog_01_enter), whose outcome
    /// transfers to the next dream (DreamPreset.Outcome transferToDream). The
    /// outcomes screen (EpilogueOutcomes, in the last locations) then shows
    /// what became of each character from the story flags, and goToCredits
    /// loads the credits, which end at the main menu.
    ///
    /// In co-op the host's game starts the epilogue as in single player and
    /// sends GameEnd (kind 1, phase 0). The session stays up: each joiner saves
    /// its character, goes into the same epilogue dream the way the startDream
    /// step does, and the epilogue is a shared dream (DreamSync.Shared): any
    /// player's endDream moves everyone on to the same next part, and the host
    /// takes a joiner's only for an outcome the current part has. The flags are
    /// the host's (FlagsSync), so the outcomes are the same. Nobody new can
    /// join while it plays.
    ///
    /// The burning forest (in the crater, after the Being is set on fire): a
    /// player's first death there starts their crawl, the second ends it, and
    /// 16 s later the game pans the camera over the burning forest (the world
    /// event epilogue_cameraPanOverBurningForest), then shows the outcomes. In
    /// co-op that pan waits: a joiner's second death tells the host
    /// (EpilogueStep 3), the dead player watches the ones still crawling
    /// (Spectator.BeginWatch, not the permadeath path), and once every player
    /// has crawled to their death, or a minute after the first did, the host
    /// sends EpilogueStep 1 and every game runs its own pan once. The host's
    /// outcomes (burned forest or not) go with EpilogueStep 2, and a joiner's
    /// outcomes screen uses them. When the host's game goes to the credits it
    /// sends GameEnd (kind 1, phase 1) and stops the server; each joiner then
    /// goes to the credits too (if its own outcomes didn't take it there first)
    /// and leaves for the main menu. A joiner that gets to the credits first
    /// leaves the session then.
    /// </summary>
    internal sealed class EpilogueSync
    {
        private readonly CoopSession _coop;

        public EpilogueSync(CoopSession coop)
        {
            _coop = coop;
            coop.Handle(MsgType.EpilogueStep, OnStep);
            coop.Net.OnPeerDisconnected += OnPeerLeft;
            coop.Hook(() => coop.Net.OnPeerDisconnected -= OnPeerLeft);
            coop.Hook(Ended);
        }

        private Session S => _coop.Net;

        public const string PanEvent = EpilogueRules.PanEvent;

        /// <summary>
        /// The crater's events that set the Being on fire: the burned-forest
        /// ending, once they run.
        /// </summary>
        public const string BurnEvents = EpilogueRules.BurnEvents;

        /// <summary>
        /// The shared epilogue is on (host or joiner): from GameEnd phase 0
        /// until the credits.
        /// </summary>
        public bool Playing { get; private set; }
        private bool _ownPan, _toMenu, _panGo, _ownDeath, _burnShared;
        /// <summary>
        /// Where our player crawled to their death (the pan starts there).
        /// </summary>
        private Vector3 _deathAt;
        /// <summary>
        /// Host: who has crawled to their death (our own LocalId too), and
        /// whether anyone has.
        /// </summary>
        private readonly CrawlTally _crawled = new CrawlTally();
        /// <summary>
        /// Joiner: the host's EpilogueOutcomes.burnedForest, once heard.
        /// </summary>
        private HostBurned _hostBurned;

        /// <summary>
        /// Host: a player who left may have been the last one still crawling.
        /// </summary>
        private void OnPeerLeft(ulong peer)
        {
            if (Playing && S != null && S.IsHost)
            {
                CheckPan();
            }
        }

        /// <summary>
        /// The session's end. If the host went away in the middle of the
        /// epilogue, our game plays it to the end on its own, and the co-op
        /// panel opens at the main menu after the credits.
        /// </summary>
        private void Ended()
        {
            if (Playing && S != null && S.IsClient)
            {
                Plugin.Instance.StartCoroutine(PanelAfterCredits());
            }

            Playing = false;
            Reset();
        }

        private void Reset()
        {
            _ownPan = _panGo = _ownDeath = _burnShared = false;
            _crawled.Reset();
            _hostBurned = HostBurned.Unheard;
            _coop.Spectator.EndWatch();
        }

        /// <summary>
        /// A joiner whose host left mid-epilogue: once the game's own credits
        /// are over, the co-op panel.
        /// </summary>
        private IEnumerator PanelAfterCredits()
        {
            Plugin.Log.LogInfo(
                "[epilogue] the host left during the epilogue: we finish it "
                    + "alone, then the co-op panel");
            Plugin.Notice(
                "The host left. The ending goes on in your game alone.");
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
        /// Our world is in the epilogue on its own (a save made at its start,
        /// or played into it before hosting): it can't be shared then, as
        /// joiners go into it only with the host, at its start.
        /// </summary>
        internal static bool InEpilogueAlone()
        {
            bool playing = false;
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                playing = coop.Epilogue.Playing;
            }

            Player p = Player.Instance;
            bool inEpilogue = p != null && p.inEpilogue;
            Story.DreamFacts dreams =
                Prologue.DreamsOf(Singleton<Dreams>.Instance);
            return EpilogueRules.InEpilogueAlone(playing, inEpilogue, dreams);
        }

        /// <summary>
        /// Host: why a new player can't join now (null: they can).
        /// </summary>
        internal string JoinRefusal()
        {
            bool host = S != null && S.IsHost;
            Verdict verdict = EpilogueRules.JoinRefusal(Playing, host);
            if (!verdict.Refused)
            {
                return null;
            }

            return Refusals.Text(verdict);
        }

        private PacketWriter GameEnd(byte phase)
        {
            var w = S.Begin(MsgType.GameEnd);
            w.W.Write((byte)Chapters.EndKind.Epilogue);
            w.W.Write(phase);
            return w;
        }

        // ---- host -----------------------------------------------------------

        /// <summary>
        /// Host: our game is preparing the epilogue dream. The players in our
        /// world come along.
        /// </summary>
        internal void HostStarts()
        {
            Playing = true;
            Reset();
            _coop.Dreaming.Share(Chapters.EpilogueDream);

            Plugin.Log.LogInfo(
                "[epilogue] the host starts the epilogue: the players in its "
                    + "world play it too");
            S.Broadcast(GameEnd(0), Channel.Control, reliable: true);
            if (Chapters.TestQuick)
            {
                Plugin.Instance.StartCoroutine(TestHostQuick());
            }
        }

        /// <summary>
        /// Test: once the epilogue is on, go to the credits (as
        /// EpilogueOutcomes.goToCredits does).
        /// </summary>
        private IEnumerator TestHostQuick()
        {
            float until = Time.realtimeSinceStartup + 60f;
            while (Time.realtimeSinceStartup < until
                && !(Player.Instance != null && Player.Instance.inEpilogue))
            {
                yield return null;
            }

            Plugin.Log.LogInfo(
                "[epilogue] test: in the epilogue: "
                    + (Player.Instance != null && Player.Instance.inEpilogue)
                    + $", shared dream {DreamSync.SharedNow}");
            yield return new WaitForSecondsRealtime(8f);
            Plugin.Log.LogInfo("[epilogue] test: skipping to the credits");
            BeforeCredits();
            Chapters.ToCredits();
            yield return Chapters.ToMenuAfterEnding();
        }

        /// <summary>
        /// Host: a joiner's dream ended with this outcome. In the epilogue,
        /// only one its current part has.
        /// </summary>
        internal bool OutcomeAllowed(Dreams d, string outcome)
        {
            List<string> outcomes = OutcomeNamesOf(d);
            if (EpilogueRules.OutcomeAllowed(Playing, outcomes, outcome))
            {
                return true;
            }

            Plugin.Log.LogWarning(
                $"[epilogue] refused a dream end: {d.preset.name} has no "
                    + $"outcome \"{outcome}\"");
            return false;
        }

        /// <summary>
        /// The only code reading a dream's outcomes for the rules: the names
        /// of its preset's outcomes (null: no Dreams or no preset).
        /// </summary>
        private List<string> OutcomeNamesOf(Dreams d)
        {
            if (d == null || d.preset == null)
            {
                return null;
            }

            var names = new List<string>();
            foreach (var o in d.preset.outcomes)
            {
                if (o != null)
                {
                    names.Add(o.name);
                }
            }

            return names;
        }

        // ---- both -----------------------------------------------------------

        /// <summary>
        /// EpilogueOutcomes.goToCredits is about to run in our game.
        /// </summary>
        internal void BeforeCredits()
        {
            var s = S;
            bool session = s != null && s.Active;
            bool host = session && s.IsHost;
            CreditsStep step =
                EpilogueRules.BeforeCredits(session, Playing, host);
            if (step == CreditsStep.None)
            {
                return;
            }

            Playing = false;
            if (step == CreditsStep.HostStops)
            {
                Plugin.Log.LogInfo(
                    "[epilogue] to the credits: the players go too, and the "
                        + "server stops");
                WorldShare instance = WorldShare.Instance;
                if (instance != null)
                {
                    instance.StopServer(GameEnd(1));
                }

                return;
            }

            Plugin.Log.LogInfo(
                "[epilogue] our outcomes are over: to the credits, leaving the "
                    + "session");
            Leave("The game has ended.");
        }

        private void Leave(string why)
        {
            Session s = S;
            if (s != null)
            {
                INetBackend backend = s.Backend;
                if (backend != null)
                {
                    backend.Leave();
                }
            }

            WorldShare instance = WorldShare.Instance;
            if (instance != null)
            {
                instance.SetLoadStatus(why);
            }

            if (_toMenu)
            {
                return;
            }

            _toMenu = true;
            Plugin.Instance.StartCoroutine(ToMenu());
        }

        private IEnumerator ToMenu()
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
        /// Events.fireWorldEvent: the game's own pan over the burning forest
        /// (16 s after its player crawled to their death) waits in co-op until
        /// the host says (EpilogueStep 1). False holds it back.
        /// </summary>
        internal bool WorldEventFired(string type)
        {
            var facts = new PanEventFacts
            {
                Playing = Playing,
                IsPan = type == PanEvent,
                InSession = S != null && S.Active,
                Released = _panGo,
                PanRan = _ownPan,
            };

            switch (EpilogueRules.WorldEvent(facts))
            {
                case PanEventStep.Held:
                    // once: ours (RunPan) or the game's own, whichever comes
                    // first
                    return false;
                case PanEventStep.RunsFirst:
                    _ownPan = true;
                    return true;
                case PanEventStep.Waits:
                    Plugin.Log.LogInfo(
                        "[epilogue] our pan over the burning forest waits "
                            + "for the others");
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>
        /// Player.die has run: if that was the second death of the crawl in the
        /// burning forest, tell the host.
        /// </summary>
        internal void AfterDie(Player p)
        {
            var s = S;
            if (!EpilogueRules.CountsDeath(DeathOf(p)))
            {
                return;
            }

            _ownDeath = true;
            _deathAt = p.transform.position;
            Plugin.Log.LogInfo(
                "[epilogue] our player crawled to their death in the burning "
                    + "forest");
            Plugin.Instance.StartCoroutine(WatchSoon());
            if (s.IsHost)
            {
                Crawled(s.LocalId);
                return;
            }

            var w = s.Begin(MsgType.EpilogueStep);
            EpilogueStepMessage.WriteCrawled(w);
            s.SendToHost(w, Channel.Control, reliable: true);
        }

        /// <summary>
        /// The only code reading the game's Player for the death rule.
        /// </summary>
        private DeathFacts DeathOf(Player p)
        {
            bool has = p != null;
            return new DeathFacts
            {
                Playing = Playing,
                Counted = _ownDeath,
                InSession = S != null && S.Active,
                HasPlayer = has,
                InEpilogue = has && p.inEpilogue,
                Crawling = has && p.crawling,
                Dying = has && p.dying,
            };
        }

        /// <summary>
        /// After the game's own 8 s on the body, watch the players still
        /// crawling, until the pan.
        /// </summary>
        private IEnumerator WatchSoon()
        {
            yield return new WaitForSeconds(8.5f);
            Spectating.Spectator spectator = _coop.Spectator;
            if (EpilogueRules.WatchesSoon(Playing, _panGo, spectator.Active))
            {
                spectator.BeginWatch();
            }
        }

        /// <summary>Host: this player crawled to their death.</summary>
        private void Crawled(ulong who)
        {
            CrawlStep step = _crawled.Crawled(who);
            if (step == CrawlStep.Repeat)
            {
                return;
            }

            if (step == CrawlStep.First)
            {
                Plugin.Instance.StartCoroutine(PanTimeout());
            }

            CheckPan();
        }

        private IEnumerator PanTimeout()
        {
            yield return new WaitForSecondsRealtime(EpilogueRules.PanWait);
            if (EpilogueRules.PanTimesOut(Playing, _panGo))
            {
                Plugin.Log.LogInfo(
                    $"[epilogue] {EpilogueRules.PanWait:0} s since the first "
                        + "death in the burning forest: the pan runs for "
                        + "everyone");
                ReleasePan();
            }
        }

        /// <summary>
        /// Host: once every player has crawled to their death, the pan, in
        /// every game.
        /// </summary>
        private void CheckPan()
        {
            var s = S;
            if (s == null)
            {
                return;
            }

            var facts = new PanFacts
            {
                Released = _panGo,
                Playing = Playing,
                IsHost = s.IsHost,
                Host = s.LocalId,
                Peers = s.Peers,
            };

            if (!_crawled.ReleasesPan(facts))
            {
                return;
            }

            Plugin.Log.LogInfo(
                "[epilogue] every player has crawled to their death: the pan "
                    + "runs for everyone");
            ReleasePan();
        }

        private void ReleasePan()
        {
            var s = S;
            var w = s.Begin(MsgType.EpilogueStep);
            EpilogueStepMessage.WritePan(w);
            s.Broadcast(w, Channel.Control, reliable: true);
            // the pan belongs to the burned forest: its outcomes follow
            SendOutcomes(true);
            RunPan();
        }

        /// <summary>
        /// The game's pan, as Player.die does it after its first 8 s: the
        /// camera drifts away from where our player died over 30 s, and 8 s in,
        /// the world event shows the burning forest and then the outcomes.
        /// </summary>
        private void RunPan()
        {
            if (_panGo)
            {
                return;
            }

            _panGo = true;
            _coop.Spectator.EndWatch();

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
                // where the game keeps a dead player
                p._transform.position = from - new Vector3(0f, 10f, 0f);
            }

            Plugin.Instance.StartCoroutine(Pan(from));
        }

        private IEnumerator Pan(Vector3 from)
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
                // ease in and out
                follow.transform.position = from - new Vector3(
                    4000f * (0.5f - 0.5f * Mathf.Cos(k * Mathf.PI)), 0f, 0f);
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
                            Plugin.Log.LogWarning(
                                "[epilogue] couldn't run the pan: "
                                    + $"{e.Message}");
                        }
                    }
                }

                yield return null;
            }
        }

        /// <summary>
        /// Host: our outcomes (the forest burned or not) go to the joiners,
        /// whose screens use them.
        /// </summary>
        internal void SendOutcomes(bool burned)
        {
            var s = S;
            if (s == null
                || !EpilogueRules.SendsOutcomes(
                    Playing, s.IsHost, s.Peers.Count))
            {
                return;
            }

            var w = s.Begin(MsgType.EpilogueStep);
            EpilogueStepMessage.WriteOutcomes(w, burned);
            s.Broadcast(w, Channel.Control, reliable: true);
        }

        /// <summary>
        /// EpilogueOutcomes.Start: the host sends its outcomes; a joiner's
        /// screen takes the host's.
        /// </summary>
        internal void OutcomesStart(EpilogueOutcomes o)
        {
            switch (EpilogueRules.OutcomesStart(OutcomesOf(o)))
            {
                case OutcomeStep.Send:
                    SendOutcomes(o.burnedForest);
                    return;
                case OutcomeStep.TakeHost:
                    bool hosts = _hostBurned == HostBurned.Yes;
                    Plugin.Log.LogWarning(
                        "[epilogue] our outcomes screen had burnedForest "
                            + $"{o.burnedForest}, the host's {hosts}: using "
                            + "the host's");
                    o.burnedForest = hosts;
                    return;
                default:
                    return;
            }
        }

        /// <summary>
        /// The only code reading the game's EpilogueOutcomes for the rules.
        /// </summary>
        private OutcomeFacts OutcomesOf(EpilogueOutcomes o)
        {
            var s = S;
            bool session = s != null && s.Active;
            return new OutcomeFacts
            {
                Exists = o != null,
                Playing = Playing,
                InSession = session,
                IsHost = session && s.IsHost,
                Host = _hostBurned,
                Burned = o != null && o.burnedForest,
            };
        }

        /// <summary>
        /// Dreams.startDreaming in the epilogue: every game puts its player at
        /// the part's start, so a joiner moves to a free spot beside the others
        /// (Sync.Players.SpawnSpot) once the part is in place.
        /// </summary>
        internal void PartStarted()
        {
            var s = S;
            if (s == null || !EpilogueRules.OffsetsPart(Playing, s.IsClient))
            {
                return;
            }

            Plugin.Instance.StartCoroutine(OffsetSoon());
        }

        private IEnumerator OffsetSoon()
        {
            yield return new WaitForSecondsRealtime(0.5f);
            var p = Player.Instance;
            var sync = Plugin.Instance.Host.Players;
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
            Vector3 to = Sync.Players.SpawnSpot.Find(p, at, taken);
            if (!EpilogueRules.Moves(VecOf(at), VecOf(to)))
            {
                yield break;
            }

            Plugin.Log.LogInfo(
                "[epilogue] the part starts where another player stands: ours "
                    + $"moves over by {(to - at).magnitude:0}");
            p._transform.position = to;
            var body = p.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = to;
            }
        }

        private static Vec3 VecOf(Vector3 v) => new Vec3(v.x, v.y, v.z);

        // ---- joiner ---------------------------------------------------------

        /// <summary>
        /// GameEnd from the host. Returns false if it isn't the shared
        /// epilogue's (Chapters handles it).
        /// </summary>
        internal bool OnGameEnd(byte kind, byte phase)
        {
            var p = Player.Instance;
            var facts = new GameEndFacts
            {
                // Chapters' handler took it only from the host
                FromHost = true,
                Kind = kind,
                Phase = phase,
                Playing = Playing,
                AtCredits = SceneManager.GetActiveScene().name == "credits",
                InWorld = WorldShare.InWorld,
                InEpilogue = p != null && p.inEpilogue,
            };

            GameEndStep step = EndingRules.GameEnd(facts);
            if (step == GameEndStep.Ignored || step == GameEndStep.Solo)
            {
                return false;
            }

            if (step == GameEndStep.BeginEpilogue
                || step == GameEndStep.JoinEpilogue)
            {
                Plugin.Log.LogInfo(
                    "[epilogue] the host starts the epilogue: we go in too");
                Playing = true;
                Reset();
                // before the epilogue's own save
                Characters instance = Characters.Instance;
                if (instance != null)
                {
                    instance.SaveNow();
                }

                Plugin.Notice(
                    "The host has reached the end of the game: the epilogue "
                        + "begins");
                if (step == GameEndStep.JoinEpilogue)
                {
                    Plugin.Instance.StartCoroutine(JoinEpilogue());
                }

                return true;
            }

            Plugin.Log.LogInfo(
                "[epilogue] the host went to the credits: so do we");
            Playing = false;
            Leave("The host reached the end of the game.");
            if (step == GameEndStep.LeaveToCredits)
            {
                Chapters.ToCredits();
            }

            return true;
        }

        /// <summary>
        /// Joiner: into the host's epilogue, once our game can (not mid-dream,
        /// not loading).
        /// </summary>
        private IEnumerator JoinEpilogue()
        {
            yield return new WaitForSecondsRealtime(1f); // the notice
            float until = Time.realtimeSinceStartup + EndingRules.TryFor;
            bool started = false;
            while (!started && Playing && Time.realtimeSinceStartup < until)
            {
                try
                {
                    started = StartHere();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"[epilogue] couldn't start the epilogue: {e.Message}");
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
                until = Time.realtimeSinceStartup + EndingRules.EnterFor;
                while (Time.realtimeSinceStartup < until && Playing
                    && !(Player.Instance != null && Player.Instance.inEpilogue))
                {
                    yield return null;
                }
            }

            bool inEpilogue =
                Player.Instance != null && Player.Instance.inEpilogue;
            switch (EndingRules.JoinEnds(Playing, inEpilogue))
            {
                case JoinEnd.Joined:
                    Plugin.Log.LogInfo(
                        "[epilogue] playing the epilogue together (shared "
                            + $"dream {DreamSync.SharedNow})");
                    yield break;
                case JoinEnd.Stopped:
                    yield break;
            }

            Plugin.Log.LogWarning(
                "[epilogue] the epilogue didn't start here: leaving, the end "
                    + "screen instead");
            Playing = false;
            Chapters.SoloEnding(Chapters.EndKind.EndScreen);
        }

        /// <summary>
        /// As the epilogue's startDream step does it: inputs off, a black
        /// screen, then the epilogue dream.
        /// </summary>
        private bool StartHere()
        {
            var dreams = Singleton<Dreams>.Instance;
            var p = Player.Instance;
            if (!EndingRules.MayStartShared(StartOf(dreams, p)))
            {
                return false;
            }

            _coop.Dreaming.Share(Chapters.EpilogueDream);

            Core.forbidInputs = true;
            p.halt();
            dreams.wantToDream = true;
            Singleton<UI>.Instance.tweenBlackScreenTop(Color.black, 2f);
            Plugin.Instance.StartCoroutine(PrepareSoon(dreams));
            return true;
        }

        /// <summary>
        /// The only code reading the game for whether the epilogue can start
        /// here.
        /// </summary>
        private StartFacts StartOf(Dreams dreams, Player p)
        {
            var o = Singleton<OutsideLocations>.Instance;
            return new StartFacts
            {
                Dreams = Prologue.DreamsOf(dreams),
                HasPlayer = p != null,
                PlaceLoading = o != null && o.loading,
                Loading = Core.loadingGame,
            };
        }

        private IEnumerator PrepareSoon(Dreams dreams)
        {
            yield return new WaitForSecondsRealtime(2.1f);
            var o = Singleton<OutsideLocations>.Instance;
            // The road home is where the last event runs; the game destroys it
            // before the epilogue.
            if (o != null
                && o.spawnedLocations.ContainsKey("outside_roadToHome_01"))
            {
                o.destroyLocation("outside_roadToHome_01");
            }

            yield return null;
            yield return null;
            dreams.StartCoroutine(dreams.prepareDream(Chapters.EpilogueDream));
        }

        private void OnStep(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null)
            {
                return;
            }

            EpilogueNote note = EpilogueStepMessage.Read(r);
            var facts = new EpilogueStepFacts
            {
                Playing = Playing,
                IsHost = s.IsHost,
                FromPeer = s.Peers.Contains(from),
                FromHost = from == s.HostId,
                Step = note.Step,
            };

            switch (EpilogueRules.OnStep(facts))
            {
                case StepHandling.Burned:
                    if (s.IsHost)
                    {
                        Plugin.Log.LogInfo(
                            $"[epilogue] {s.NameOf(from)} set the Being on "
                                + "fire");
                        Burned(from);
                        return;
                    }

                    Burned(EpilogueStepMessage.ReadWho(r));
                    return;
                case StepHandling.Crawled:
                    Plugin.Log.LogInfo(
                        $"[epilogue] {s.NameOf(from)} crawled to their death "
                            + "in the burning forest");
                    Crawled(from);
                    return;
                case StepHandling.Outcomes:
                    _hostBurned = EpilogueRules.HeardBurned(note.Value);
                    return;
                case StepHandling.Pan:
                    Plugin.Log.LogInfo(
                        "[epilogue] the host says the camera pans over the "
                            + "burning forest: ours too");
                    RunPan();
                    return;
                default:
                    return;
            }
        }

        // ---- the ending's choice --------------------------------------------

        /// <summary>
        /// GameEvents.fire: the crater's events that set the Being on fire ran
        /// in our game (our player burned it). That chooses the burned-forest
        /// ending for everyone: the others' games run the same events, so
        /// they're shut in the fire too. (The other ending, going back to the
        /// room and lying down to sleep, is a dream's end and moves everyone on
        /// through the shared dream.)
        /// </summary>
        internal void EventsFired(GameEvents ge)
        {
            var s = S;
            string name = null;
            if (ge != null)
            {
                name = ge.name;
            }

            var facts = new BurnShareFacts
            {
                Playing = Playing,
                Shared = _burnShared,
                Name = name,
                Replaying = _coop.Guards.Replaying,
                InSession = s != null && s.Active,
            };

            if (!EpilogueRules.SharesBurn(facts))
            {
                return;
            }

            _burnShared = true;
            Plugin.Log.LogInfo(
                "[epilogue] our player set the Being on fire: the forest burns "
                    + "for everyone");
            var w = s.Begin(MsgType.EpilogueStep);
            if (s.IsHost)
            {
                EpilogueStepMessage.WriteBurnBy(w, s.LocalId);
                s.Broadcast(w, Channel.Control, reliable: true);
            }
            else
            {
                EpilogueStepMessage.WriteBurn(w);
                s.SendToHost(w, Channel.Control, reliable: true);
            }
        }

        /// <summary>
        /// Someone else set the Being on fire: run the same events here (the
        /// host passes it on first).
        /// </summary>
        private void Burned(ulong who)
        {
            var s = S;
            if (s == null)
            {
                return;
            }

            GameEvents found = BurnEventsHere();
            BurnStep step = EpilogueRules.Burned(BurnOf(found));
            if (step == BurnStep.Ignored)
            {
                return;
            }

            _burnShared = true;
            if (s.IsHost)
            {
                var w = s.Begin(MsgType.EpilogueStep);
                EpilogueStepMessage.WriteBurnBy(w, who);
                s.Broadcast(w, Channel.Control, reliable: true, except: who);
            }

            Plugin.Notice(
                $"{s.NameOf(who)} set the Being on fire. The forest burns.");
            if (step == BurnStep.Missing)
            {
                Plugin.Log.LogWarning(
                    "[epilogue] the Being burns in another game, but our part "
                        + "has no such events");
                return;
            }

            if (step == BurnStep.AlreadyFired)
            {
                return;
            }

            Plugin.Log.LogInfo(
                $"[epilogue] {s.NameOf(who)} set the Being on fire: running "
                    + $"{BurnEvents} here too");
            _coop.Guards.Replaying = true;
            try
            {
                found.fire();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"[epilogue] couldn't run {BurnEvents}: {e.Message}");
            }
            finally
            {
                _coop.Guards.Replaying = false;
            }
        }

        /// <summary>
        /// The crater's events in our part (null: none).
        /// </summary>
        private GameEvents BurnEventsHere()
        {
            foreach (var ge in UnityEngine.Object
                .FindObjectsOfType<GameEvents>(true))
            {
                if (ge != null && ge.name == BurnEvents)
                {
                    return ge;
                }
            }

            return null;
        }

        /// <summary>
        /// The only code reading the crater's GameEvents for the fire rule.
        /// </summary>
        private BurnFacts BurnOf(GameEvents found)
        {
            bool has = found != null;
            return new BurnFacts
            {
                Shared = _burnShared,
                Found = has,
                Fired = has && found.fired,
                Multiple = has && found.multipleFire,
            };
        }

        // ---- for the tests ----

        public bool BurnShared => _burnShared;

        public bool PanReleased => _panGo;
        public bool OwnDeath => _ownDeath;
        public HostBurned HostBurned => _hostBurned;
    }

    [HarmonyPatch(typeof(EpilogueOutcomes), "goToCredits")]
    static class EpilogueCreditsPatch
    {
        static void Prefix()
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Epilogue.BeforeCredits();
            }
        }
    }

    [HarmonyPatch(
        typeof(Events), nameof(Events.fireWorldEvent),
        new[] { typeof(string) })]
    static class EpilogueWorldEventPatch
    {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(string type)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return coop.Epilogue.WorldEventFired(type);
        }
    }

    [HarmonyPatch(typeof(GameEvents), nameof(GameEvents.fire))]
    static class EpilogueBurnPatch
    {
        static void Postfix(GameEvents __instance)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Epilogue.EventsFired(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.die))]
    static class EpilogueDiePatch
    {
        static void Postfix(Player __instance)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Epilogue.AfterDie(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(EpilogueOutcomes), "Start")]
    static class EpilogueOutcomesPatch
    {
        static void Prefix(EpilogueOutcomes __instance)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Epilogue.OutcomesStart(__instance);
            }
        }
    }

    /// <summary>
    /// The epilogue's parts: a joiner starts each one beside the others, not on
    /// top of them.
    /// </summary>
    [HarmonyPatch(typeof(Dreams), nameof(Dreams.startDreaming))]
    static class EpilogueSpawnPatch
    {
        static void Postfix()
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Epilogue.PartStarted();
            }
        }
    }
}
