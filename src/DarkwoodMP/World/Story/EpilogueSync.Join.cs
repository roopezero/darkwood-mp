using System;
using System.Collections;
using System.Collections.Generic;
using DarkwoodMP.Net;
using DarkwoodMP.World.Dreaming;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DarkwoodMP.World.Story
{
    /// <summary>
    /// The joiner's side of the epilogue: into the host's epilogue, each
    /// part beside the others, and out at the credits.
    /// </summary>
    internal sealed partial class EpilogueSync
    {
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
            Plugin.Instance.StartCoroutine(AskLayoutSoon());
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

        // ---- host: late joiners --------------------------------------------

        /// <summary>
        /// Each frame (Plugin.Update), host: a world in the epilogue alone
        /// becomes the shared epilogue.
        /// </summary>
        internal void Update()
        {
            var s = S;
            bool host = s != null && s.IsHost;
            if (!EpilogueRules.Adopts(host, InEpilogueAlone(), _over))
            {
                return;
            }

            Playing = true;
            Reset();
            _coop.Dreaming.Share(StoryRequestRules.EpilogueDream);
            // our last save is the one on the way into the first part
            _coop.Story.NoteSnapshot();
            Plugin.Log.LogInfo(
                "[epilogue] our world is in the epilogue: it is shared, and "
                    + "players who join go into it");
        }

        /// <summary>
        /// Host: a player was let in while we play the epilogue: tell them
        /// the part we're in, which their game goes into.
        /// </summary>
        private void OnPeerJoined(ulong peer)
        {
            var s = S;
            if (!Playing || s == null || !s.IsHost)
            {
                return;
            }

            string part = EpilogueRules.PartToJoin(
                Prologue.DreamsOf(Singleton<Dreams>.Instance));
            Plugin.Log.LogInfo(
                $"[epilogue] {s.NameOf(peer)} joins the epilogue, in {part}");
            var w = s.Begin(MsgType.EpilogueStep);
            EpilogueStepMessage.WriteJoinPart(w, part);
            s.SendTo(peer, w, Channel.Control, reliable: true);
        }

        // ---- joiner ---------------------------------------------------------

        /// <summary>
        /// Joiner: the host plays the epilogue, in this part. We play it too:
        /// the save we load goes back into the epilogue's dream as it loads
        /// (LoadedPart puts the host's part in place of its first), or, if
        /// our world isn't in it, we go in the way the startDream step does.
        /// </summary>
        private void JoinPart(string part)
        {
            Plugin.Log.LogInfo(
                $"[epilogue] the host is playing the epilogue, in {part}: we "
                    + "join them there");
            Playing = true;
            Reset();
            _joinPart = part;
            _coop.Dreaming.Share(StoryRequestRules.EpilogueDream);
            Plugin.Notice(
                "The host is playing the epilogue: you join them in it");
            Plugin.Instance.StartCoroutine(LateJoin(part));
        }

        /// <summary>
        /// Dreams.prepareDream is about to run with this preset: the one to
        /// run instead (the host's part, when a save we load goes back into
        /// the epilogue as we join it).
        /// </summary>
        internal string LoadedPart(Dreams d, string preset)
        {
            bool loading = d != null && d.loadingSaveGameInDream;
            string part = EpilogueRules.LoadedPart(_joinPart, preset, loading);
            if (part != preset)
            {
                Plugin.Log.LogInfo(
                    $"[epilogue] the save goes back into {preset}: into the "
                        + $"host's {part} instead");
            }

            return part;
        }

        /// <summary>
        /// Joiner, let in while the host plays the epilogue: once our world
        /// has loaded (or its save took us into the dream), into the host's
        /// part.
        /// </summary>
        private IEnumerator LateJoin(string part)
        {
            float until = Time.realtimeSinceStartup + LoadFor;
            while (Playing && Time.realtimeSinceStartup < until
                && !WorldShare.WorldReady && !InEpilogueNow())
            {
                yield return null;
            }

            yield return JoinEpilogue(part);
            _joinPart = null;
        }

        /// <summary>
        /// How long a late joiner waits for the host's world to load.
        /// </summary>
        private const float LoadFor = 300f;

        private static bool InEpilogueNow()
        {
            var p = Player.Instance;
            return p != null && p.inEpilogue;
        }

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
                _coop.Characters.SaveNow();

                Plugin.Notice(
                    "The host has reached the end of the game: the epilogue "
                        + "begins");
                if (step == GameEndStep.JoinEpilogue)
                {
                    Plugin.Instance.StartCoroutine(
                        JoinEpilogue(StoryRequestRules.EpilogueDream));
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
        /// Joiner: into the host's epilogue part, once our game can (not
        /// mid-dream, not loading). A game already going into it by itself
        /// (a save loading in its dream) isn't started again.
        /// </summary>
        private IEnumerator JoinEpilogue(string part)
        {
            yield return new WaitForSecondsRealtime(1f); // the notice
            float until = Time.realtimeSinceStartup + EndingRules.TryFor;
            bool started = false;
            while (!started && Playing && Time.realtimeSinceStartup < until
                && !InEpilogueNow())
            {
                try
                {
                    started = StartHere(part);
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

            if (started || InEpilogueNow())
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
                            + $"dream {_coop.Dreaming.Shared})");
                    yield break;
                case JoinEnd.Stopped:
                    yield break;
            }

            Plugin.Log.LogWarning(
                "[epilogue] the epilogue didn't start here: leaving, the end "
                    + "screen instead");
            Playing = false;
            Chapters.SoloEnding(EndKind.EndScreen);
        }

        /// <summary>
        /// As the epilogue's startDream step does it: inputs off, a black
        /// screen, then the epilogue dream (this part of it).
        /// </summary>
        private bool StartHere(string part)
        {
            var dreams = Singleton<Dreams>.Instance;
            var p = Player.Instance;
            if (!EndingRules.MayStartShared(StartOf(dreams, p)))
            {
                return false;
            }

            _coop.Dreaming.Share(StoryRequestRules.EpilogueDream);

            Core.forbidInputs = true;
            p.halt();
            dreams.wantToDream = true;
            Singleton<UI>.Instance.tweenBlackScreenTop(Color.black, 2f);
            Plugin.Instance.StartCoroutine(PrepareSoon(dreams, part));
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

        private IEnumerator PrepareSoon(Dreams dreams, string part)
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
            dreams.StartCoroutine(dreams.prepareDream(part));
        }
    }
}
