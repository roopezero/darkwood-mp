using System;
using System.Collections;
using System.Collections.Generic;
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
            Chapters.SoloEnding(EndKind.EndScreen);
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

            _coop.Dreaming.Share(StoryRequestRules.EpilogueDream);

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
            dreams.StartCoroutine(
                dreams.prepareDream(StoryRequestRules.EpilogueDream));
        }
    }
}
