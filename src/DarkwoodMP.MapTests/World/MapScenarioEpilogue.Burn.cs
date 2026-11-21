using DarkwoodMP.World.Story;
using HarmonyLib;
using System.Collections;
using UnityEngine.SceneManagement;
using UnityEngine;
using Chapters = DarkwoodMP.World.Story.Chapters;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// The epilogue's steps from the crater on: the burning, the crawl, the
    /// pan and leaving the credits.
    public static partial class MapScenarioEpilogue
    {
        /// <summary>
        /// "epilogue-at PART" (host): the client's ends took us to PART too.
        /// </summary>
        private static IEnumerator At(string[] a, Result r)
        {
            string want = "crater";
            if (a.Length > 0)
            {
                want = a[0];
            }

            float until = Time.realtimeSinceStartup + 300f;
            while (Time.realtimeSinceStartup < until && !InPartLike(want))
            {
                yield return new WaitForSeconds(0.5f);
            }

            yield return new WaitForSeconds(3f);
            r.Info = $"{Where()}; a friend's puppet shown {FriendShown()}";
            if (!InPartLike(want))
            {
                r.Fail($"not in {want}: {r.Info}");
            }
        }

        private static bool InPartLike(string name) => P != null
            && P.inEpilogue && D != null && D.dreaming && !D.switchingDream
            && D.preset != null && D.preset.name.Contains(name);

        /// <summary>
        /// "epilogue-burn" (host): our player sets the Being on fire (the
        /// crater's events run here).
        /// </summary>
        private static IEnumerator Burn(string[] a, Result r)
        {
            GameEvents found = null;
            foreach (var ge in Object.FindObjectsOfType<GameEvents>(true))
            {
                if (ge != null && ge.name == EpilogueSync.BurnEvents)
                {
                    found = ge;
                    break;
                }
            }

            if (found == null)
            {
                r.Fail($"no {EpilogueSync.BurnEvents} here: {Where()}");
                yield break;
            }

            AsPlayer(() => found.fire());
            yield return new WaitForSeconds(2f);
            r.Info =
                $"{EpilogueSync.BurnEvents} fired {found.fired}; shared "
                    + $"{BurnShared()}";
            if (!BurnShared())
            {
                r.Fail($"not sent: {r.Info}");
            }
        }

        /// <summary>
        /// "epilogue-burned" (client): the host's burning of the Being ran in
        /// our crater too.
        /// </summary>
        private static IEnumerator Burned(string[] a, Result r)
        {
            GameEvents found = null;
            foreach (var ge in Object.FindObjectsOfType<GameEvents>(true))
            {
                if (ge != null && ge.name == EpilogueSync.BurnEvents)
                {
                    found = ge;
                    break;
                }
            }

            float until = Time.realtimeSinceStartup + 30f;
            while (Time.realtimeSinceStartup < until
                && !(found != null && found.fired))
            {
                yield return new WaitForSeconds(0.5f);
            }

            r.Info =
                $"{EpilogueSync.BurnEvents} found {found != null} fired "
                    + $"{found != null && found.fired}; heard "
                    + $"{BurnShared()}";
            if (found == null || !found.fired)
            {
                r.Fail($"the Being didn't burn here: {r.Info}");
            }
        }

        /// <summary>
        /// "epilogue-crawl-die" (either): our player's first death in the fire
        /// starts the crawl, the second ends it (as the burning does). The pan
        /// must wait for the other player ("epilogue-pan").
        /// </summary>
        private static IEnumerator CrawlDie(string[] a, Result r)
        {
            P.die();
            float until = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < until && !P.crawling)
            {
                yield return new WaitForSeconds(0.25f);
            }

            if (!P.crawling && P.startingCrawling)
            {
                // A test shortcut: our player stands where the test put it, not
                // where the burning leaves it, and the CrawlStart clip didn't
                // play here; end it as the clip's end does (Player's
                // animation-completed handler).
                P.startingCrawling = false;
                P.performingAction = false;
                P.crawling = true;
            }

            if (!P.crawling)
            {
                r.Fail(
                    "no crawl after the first death: startingCrawling "
                        + $"{P.startingCrawling} dying {P.dying}");
                yield break;
            }

            yield return new WaitForSeconds(1f);
            P.die();
            yield return new WaitForSeconds(1f);
            r.Info =
                $"crawled {P.crawling} dying {P.dying}; our death told "
                    + $"{OwnDeath()}; pan released "
                    + $"{PanReleased()}";
            if (!OwnDeath())
            {
                r.Fail($"the second death wasn't the crawl's: {r.Info}");
            }
        }

        /// <summary>
        /// "epilogue-pan EARLY" (either): the pan ran here once every player
        /// had crawled to their death (not before: EARLY 1 checks it was still
        /// held 20 s after our own death); on the client the host's outcomes
        /// came too.
        /// </summary>
        private static IEnumerator PanStep(string[] a, Result r)
        {
            float until = Time.realtimeSinceStartup + 90f;
            while (Time.realtimeSinceStartup < until
                && !PanReleased())
            {
                yield return new WaitForSeconds(0.5f);
            }

            yield return new WaitForSeconds(12f);
            var outcomes = Object.FindObjectOfType<EpilogueOutcomes>();
            string text = "not shown";
            if (outcomes != null)
            {
                if (outcomes.burnedForest)
                {
                    text = "burned";
                }
                else
                {
                    text = "not burned";
                }
            }

            r.Info =
                $"pan released {PanReleased()}; spectating "
                    + $"{Spectating()}; host's burned "
                    + $"{HeardBurned()}; outcomes screen {text}";
            if (!PanReleased())
            {
                r.Fail($"no pan: {r.Info}");
            }
            else if (Plugin.Instance.Host.Session.IsClient
                && HeardBurned() != HostBurned.Yes)
            {
                r.Fail($"the host's outcomes didn't come: {r.Info}");
            }
            else if (outcomes != null && !outcomes.burnedForest)
            {
                r.Fail($"the outcomes aren't the burned forest's: {r.Info}");
            }
        }

        /// <summary>
        /// "epilogue-held" (host): our own crawl death alone doesn't run the
        /// pan (the client is still crawling).
        /// </summary>
        private static IEnumerator Held(string[] a, Result r)
        {
            yield return new WaitForSeconds(20f); // past the game's own 16 s
            r.Info =
                $"pan released {PanReleased()}; spectating "
                    + $"{Spectating()} (epilogue {WatchingEpilogue()})";
            if (PanReleased())
            {
                r.Fail($"the pan ran before the client died: {r.Info}");
            }
        }

        private static IEnumerator ExitCredits()
        {
            yield return new WaitForSeconds(3f);
            var c = Object.FindObjectOfType<global::Credits>();
            if (c != null)
            {
                AccessTools.Method(typeof(global::Credits), "exitToMainMenu")
                    .Invoke(c, null);
            }

            while (!WorldShare.MainMenuReady())
            {
                yield return new WaitForSeconds(0.5f);
            }
        }
    }
}
