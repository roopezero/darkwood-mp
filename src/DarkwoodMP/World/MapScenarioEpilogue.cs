using System.Collections;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps for the epilogue played together (World.EpilogueSync). The list is
    /// tools/tests/map_scenario_epilogue.py; the epilogue ends the game, so it runs alone and last.
    /// </summary>
    public static class MapScenarioEpilogue
    {
        private static Player P => Player.Instance;
        private static Dreams D => Singleton<Dreams>.Instance;

        public static void Register()
        {
            Add("epilogue-start", Start, timeout: 120f);
            Add("epilogue-in", In, timeout: 120f);
            Add("epilogue-next", Next, timeout: 120f);
            Add("epilogue-moved", Moved, timeout: 120f);
            Add("epilogue-credits", Credits, timeout: 120f);
            Add("epilogue-menu", Menu, timeout: 120f);
        }

        private static string Where() => $"preset {D?.preset?.name ?? "none"} dreaming {D != null && D.dreaming} inEpilogue {P != null && P.inEpilogue} shared {DreamSync.Shared ?? "none"} playing {EpilogueSync.Playing}";

        private static bool InPart(string name) => P != null && P.inEpilogue && D != null && D.dreaming && !D.switchingDream && D.preset != null && D.preset.name == name;

        private static bool FriendShown()
        {
            var ps = Sync.PlayerSync.Instance;
            if (ps != null) foreach (var rp in ps.Remotes.Values) if (rp.Root != null && rp.Root.activeInHierarchy) return true;
            return false;
        }

        /// <summary>"epilogue-start" (host): the game's last step starts the epilogue; wait until we're in its first part.</summary>
        private static IEnumerator Start(string[] a, Result r)
        {
            var step = new GameEvent { type = GameEvent.Type.startDream, Value = Chapters.EpilogueDream, eventAmount = 1 };
            Singleton<Controller>.Instance.StartCoroutine(step.fire(P.gameObject));
            while (!InPart(Chapters.EpilogueDream)) yield return new WaitForSeconds(0.5f);
            yield return new WaitForSeconds(2f);
            r.Info = Where();
            if (!EpilogueSync.Playing || DreamSync.Shared == null) r.Fail($"the epilogue isn't shared: {r.Info}");
        }

        /// <summary>"epilogue-in" (client): we went into the host's epilogue by ourselves, still connected.</summary>
        private static IEnumerator In(string[] a, Result r)
        {
            while (!InPart(Chapters.EpilogueDream)) yield return new WaitForSeconds(0.5f);
            yield return new WaitForSeconds(4f);
            r.Info = $"{Where()}; session {Net.Session.Instance?.Active}; a friend's puppet shown {FriendShown()}";
            if (!EpilogueSync.Playing || DreamSync.Shared == null || Net.Session.Instance == null || !Net.Session.Instance.Active)
                r.Fail($"not in the shared epilogue: {r.Info}");
        }

        /// <summary>"epilogue-next" (client): our part ends (its first outcome, as its endDream step does); we go on to the next.</summary>
        private static IEnumerator Next(string[] a, Result r)
        {
            string from = D.preset != null ? D.preset.name : "";
            if (D.preset == null || D.preset.outcomes.Count == 0) { r.Fail($"no outcomes: {Where()}"); yield break; }
            D.outcome = D.preset.outcomes[0].name;
            string outcome = D.outcome;
            AsPlayer(() => D.initiateEndDreaming());
            float until = Time.realtimeSinceStartup + 90f;
            while (Time.realtimeSinceStartup < until && !(D.dreaming && !D.switchingDream && D.preset != null && D.preset.name != from)) yield return new WaitForSeconds(0.5f);
            yield return new WaitForSeconds(2f);
            r.Info = $"ended {from} ({outcome}): {Where()}";
            if (D.preset == null || D.preset.name == from || !P.inEpilogue) r.Fail($"didn't go on: {r.Info}");
        }

        /// <summary>"epilogue-moved FROM" (host): the client's end of a part took us on too, out of FROM.</summary>
        private static IEnumerator Moved(string[] a, Result r)
        {
            string from = a.Length > 0 ? a[0] : Chapters.EpilogueDream;
            float until = Time.realtimeSinceStartup + 90f;
            while (Time.realtimeSinceStartup < until && !(D.dreaming && !D.switchingDream && D.preset != null && D.preset.name != from)) yield return new WaitForSeconds(0.5f);
            yield return new WaitForSeconds(3f);
            r.Info = $"{Where()}; a friend's puppet shown {FriendShown()}";
            if (D.preset == null || D.preset.name == from || !P.inEpilogue) r.Fail($"we stayed in {from}: {r.Info}");
        }

        /// <summary>"epilogue-credits" (host): to the credits as EpilogueOutcomes.goToCredits does; the server stops.</summary>
        private static IEnumerator Credits(string[] a, Result r)
        {
            EpilogueSync.BeforeCredits();
            Chapters.ToCredits();
            while (SceneManager.GetActiveScene().name != "credits") yield return new WaitForSeconds(0.5f);
            r.Info = $"in the credits; session {Net.Session.Instance?.Active}";
            yield return ExitCredits();
        }

        /// <summary>"epilogue-menu" (client): the host's credits took us to ours, out of the session, then the menu.</summary>
        private static IEnumerator Menu(string[] a, Result r)
        {
            bool credits = false;
            while (!WorldShare.MainMenuReady())
            {
                if (!credits && SceneManager.GetActiveScene().name == "credits") { credits = true; yield return ExitCredits(); }
                yield return new WaitForSeconds(0.5f);
            }
            r.Info = $"at the main menu (credits seen {credits}); session {Net.Session.Instance?.Active}";
            if (!credits) r.Fail($"no credits: {r.Info}");
            else if (Net.Session.Instance != null && Net.Session.Instance.Active) r.Fail($"still in the session: {r.Info}");
        }

        private static IEnumerator ExitCredits()
        {
            yield return new WaitForSeconds(3f);
            var c = Object.FindObjectOfType<global::Credits>();
            if (c != null) AccessTools.Method(typeof(global::Credits), "exitToMainMenu").Invoke(c, null);
            while (!WorldShare.MainMenuReady()) yield return new WaitForSeconds(0.5f);
        }
    }
}
