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
            var kinds = new System.Collections.Generic.List<string>();
            foreach (var t in Object.FindObjectsOfType<Transform>(true))
            {
                if (t == null || t.name != name)
                {
                    continue;
                }

                foreach (var c in t.GetComponents<Component>())
                {
                    kinds.Add(c.GetType().Name);
                }

                found = t.GetComponentInChildren<GameEvents>(true);
                if (found != null)
                {
                    break;
                }
            }

            if (found == null)
            {
                r.Fail($"no GameEvents in {name} ({string.Join(",", kinds)})"
                    + $": {Where()}");
                yield break;
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
            float until = Time.realtimeSinceStartup + 240f;
            bool seen = false;
            while (Time.realtimeSinceStartup < until
                && SceneManager.GetActiveScene().name != "credits")
            {
                var o = Object.FindObjectOfType<EpilogueOutcomes>();
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

            r.Info = $"outcomes seen {seen}, credits {credits}; session "
                + $"{active}";
            if (!credits)
            {
                r.Fail($"the outcomes didn't end in the credits: {r.Info}");
                yield break;
            }

            yield return ExitCredits();
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
