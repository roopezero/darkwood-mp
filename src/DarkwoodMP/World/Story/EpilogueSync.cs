using DarkwoodMP.World.Dreaming;
using System;
using System.Collections;
using System.Collections.Generic;
using DarkwoodMP.Net;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DarkwoodMP.World.Story
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
    internal sealed partial class EpilogueSync
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
                if (Plugin.Instance.Host.Session.Active)
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

        private PacketWriter EndMessage(byte phase)
        {
            var w = S.Begin(MsgType.GameEnd);
            GameEnd.Write(w, new EndWord
            {
                Kind = EndKind.Epilogue,
                Phase = phase,
            });

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
            _coop.Dreaming.Share(StoryRequestRules.EpilogueDream);

            Plugin.Log.LogInfo(
                "[epilogue] the host starts the epilogue: the players in its "
                    + "world play it too");
            S.Broadcast(EndMessage(0), Channel.Control, reliable: true);
            if (Plugin.Instance.Host.TestQuickEnd)
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
                    + $", shared dream {_coop.Dreaming.Shared}");
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
                WorldShare instance = Plugin.Instance.Host.Share;
                if (instance != null)
                {
                    instance.StopServer(EndMessage(1));
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

            WorldShare instance = Plugin.Instance.Host.Share;
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

        // ---- for the tests ----

        public bool BurnShared => _burnShared;

        public bool PanReleased => _panGo;
        public bool OwnDeath => _ownDeath;
        public HostBurned HostBurned => _hostBurned;
    }
}
