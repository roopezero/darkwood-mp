using System.Collections;
using DarkwoodMP.Menus;
using DarkwoodMP.World.Joining;
using UnityEngine;
using UnityEngine.SceneManagement;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// The epilogue's real end: the host goes to sleep in the room and its
    /// outcomes end in the credits by themselves; the client gets back to a
    /// main menu that works.
    public static partial class MapScenarioEpilogue
    {
        private const string SleepEvents = "epilogue_goToSleep_cutscene";

        /// <summary>
        /// "epilogue-events NAME": run the part's GameEvents NAME as our
        /// player (as the bed's use runs the sleep).
        /// </summary>
        private static IEnumerator RunEvents(string[] a, Result r)
        {
            string name = SleepEvents;
            if (a.Length > 0)
            {
                name = a[0];
            }

            GameEvents found = null;
            Location loc = null;
            if (D != null)
            {
                loc = D.dreamLocation;
            }

            if (loc == null)
            {
                r.Fail($"no part: {Where()}");
                yield break;
            }

            // the part's own, an active one first
            foreach (var ge in loc.GetComponentsInChildren<GameEvents>(true))
            {
                if (ge.name == name
                    && (found == null || ge.gameObject.activeInHierarchy))
                {
                    found = ge;
                }
            }

            if (found == null)
            {
                r.Fail($"no {name} in {loc.name}: {Where()}");
                yield break;
            }

            // its coroutines need it active (the bed's use turns it on)
            for (Transform t = found.transform; t != null
                && t != loc.transform; t = t.parent)
            {
                t.gameObject.SetActive(true);
            }

            AsPlayer(() => found.fire());
            yield return new WaitForSeconds(2f);
            r.Info = $"{name} fired {found.fired}: {Where()}";
        }

        /// <summary>
        /// "epilogue-outcomes" (host): wait for the outcomes screen, skip each
        /// outcome as a player's key does, until the screen's own
        /// goToCredits loads the credits; then leave them.
        /// </summary>
        private static IEnumerator Outcomes(string[] a, Result r)
        {
            if (a.Length > 0 && a[0] == "start")
            {
                ShowOutcomes(r);
            }

            float until = Time.realtimeSinceStartup + 240f;
            float nudge = Time.realtimeSinceStartup + 45f;
            bool seen = false;
            bool nudged = false;
            while (Time.realtimeSinceStartup < until
                && SceneManager.GetActiveScene().name != "credits")
            {
                var o = Object.FindObjectOfType<EpilogueOutcomes>();
                if (o == null && !nudged && Time.realtimeSinceStartup > nudge)
                {
                    // not come by itself: the part's own screen, turned on
                    nudged = true;
                    ShowOutcomes(r);
                }

                if (o != null)
                {
                    seen = true;
                    o.skipOutcome();
                }

                yield return new WaitForSecondsRealtime(1f);
            }

            bool credits = SceneManager.GetActiveScene().name == "credits";
            Net.Session session = Plugin.Instance.Host.Session;
            bool? active = null;
            if (session != null)
            {
                active = session.Active;
            }

            r.Info = $"outcomes seen {seen} (turned on {nudged}), "
                + $"credits {credits}; session "
                + $"{active}";
            if (!credits)
            {
                r.Fail($"the outcomes didn't end in the credits: {r.Info}");
                yield break;
            }

            yield return ExitCredits();
        }

        /// <summary>
        /// "epilogue-drop" (host): our server goes away mid-epilogue with no
        /// last word, as a crash or a lost connection would.
        /// </summary>
        private static IEnumerator Drop(string[] a, Result r)
        {
            Net.Session session = Plugin.Instance.Host.Session;
            if (session == null || !session.Active)
            {
                r.Fail("no session");
                yield break;
            }

            session.Backend.Leave();
            yield return new WaitForSeconds(1f);
            r.Info = $"session {session.Active}: {Where()}";
        }

        /// <summary>
        /// The part's own outcomes screen turned on, as the end of the sleep
        /// does (its Start shows the outcomes; their end is goToCredits).
        /// </summary>
        private static void ShowOutcomes(Result r)
        {
            Location loc = null;
            if (D != null)
            {
                loc = D.dreamLocation;
            }

            if (loc == null)
            {
                return;
            }

            var all = loc.GetComponentsInChildren<EpilogueOutcomes>(true);
            if (all.Length == 0)
            {
                r.Info = $"no outcomes screen in {loc.name}";
                return;
            }

            for (Transform t = all[0].transform; t != null
                && t != loc.transform; t = t.parent)
            {
                t.gameObject.SetActive(true);
            }
        }

        /// <summary>
        /// "epilogue-menu-works" (client): the credits, then the main menu with
        /// its entries on the screen and inputs on, out of the session.
        /// </summary>
        private static IEnumerator MenuWorks(string[] a, Result r)
        {
            bool credits = false;
            float until = Time.realtimeSinceStartup + 150f;
            while (!WorldShare.MainMenuReady()
                && Time.realtimeSinceStartup < until)
            {
                if (!credits && SceneManager.GetActiveScene().name == "credits")
                {
                    credits = true;
                    yield return ExitCredits();
                }

                yield return new WaitForSeconds(0.5f);
            }

            yield return new WaitForSecondsRealtime(4f);
            MenuList list = Plugin.Instance.Host.MenuList;
            bool shown = list.Shown;
            string check = list.Check();
            bool entries = check.Contains("Start Game at");
            r.Info = $"main menu {WorldShare.MainMenuReady()} (credits seen "
                + $"{credits}); inputs forbidden {Core.forbidInputs}; "
                + $"session {Plugin.Instance.Host.Session.Active}; {check}";
            if (!WorldShare.MainMenuReady() || !shown || !entries
                || Core.forbidInputs || Plugin.Instance.Host.Session.Active)
            {
                r.Fail($"the main menu doesn't work: {r.Info}");
            }
        }
    }
}
