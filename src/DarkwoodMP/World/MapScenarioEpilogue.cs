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
            Add("epilogue-onward", Onward, timeout: 600f);
            Add("epilogue-at", At, timeout: 400f);
            Add("epilogue-burn", Burn, timeout: 60f);
            Add("epilogue-burned", Burned, timeout: 60f);
            Add("epilogue-crawl-die", CrawlDie, timeout: 60f);
            Add("epilogue-held", Held, timeout: 60f);
            Add("epilogue-pan", PanStep, timeout: 150f);
            Add("epilogue-credits", Credits, timeout: 120f);
            Add("epilogue-menu", Menu, timeout: 120f);
        }

        private static string Where() => $"preset {D?.preset?.name ?? "none"} dreaming {D != null && D.dreaming} inEpilogue {P != null && P.inEpilogue} shared {DreamSync.Shared ?? "none"} playing {EpilogueSync.Playing}";

        private static bool InPart(string name) => P != null && P.inEpilogue && D != null && D.dreaming && !D.switchingDream && D.preset != null && D.preset.name == name;

        private static bool FriendShown()
        {
            var ps = Sync.PlayerSync.Instance;
            if (ps != null)
            {
                foreach (var rp in ps.Remotes.Values)
                {
                    if (rp.Root != null && rp.Root.activeInHierarchy)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>"epilogue-start" (host): the game's last step starts the epilogue; wait until we're in its first part.</summary>
        private static IEnumerator Start(string[] a, Result r)
        {
            var step = new GameEvent { type = GameEvent.Type.startDream, Value = Chapters.EpilogueDream, eventAmount = 1 };
            Singleton<Controller>.Instance.StartCoroutine(step.fire(P.gameObject));
            while (!InPart(Chapters.EpilogueDream))
            {
                yield return new WaitForSeconds(0.5f);
            }
            yield return new WaitForSeconds(2f);
            r.Info = Where();
            if (!EpilogueSync.Playing || DreamSync.Shared == null)
            {
                r.Fail($"the epilogue isn't shared: {r.Info}");
            }
        }

        /// <summary>"epilogue-in" (client): we went into the host's epilogue by ourselves, still connected.</summary>
        private static IEnumerator In(string[] a, Result r)
        {
            while (!InPart(Chapters.EpilogueDream))
            {
                yield return new WaitForSeconds(0.5f);
            }
            yield return new WaitForSeconds(4f);
            r.Info = $"{Where()}; session {Net.Session.Instance?.Active}; a friend's puppet shown {FriendShown()}";
            if (!EpilogueSync.Playing || DreamSync.Shared == null || Net.Session.Instance == null || !Net.Session.Instance.Active)
            {
                r.Fail($"not in the shared epilogue: {r.Info}");
            }
        }

        /// <summary>"epilogue-next" (client): our part ends (its first outcome, as its endDream step does); we go on to the next.</summary>
        private static IEnumerator Next(string[] a, Result r)
        {
            string from = D.preset != null ? D.preset.name : "";
            if (D.preset == null || D.preset.outcomes.Count == 0)
            {
                r.Fail($"no outcomes: {Where()}");
                yield break;
            }
            D.outcome = D.preset.outcomes[0].name;
            string outcome = D.outcome;
            AsPlayer(() => D.initiateEndDreaming());
            float until = Time.realtimeSinceStartup + 90f;
            while (Time.realtimeSinceStartup < until && !(D.dreaming && !D.switchingDream && D.preset != null && D.preset.name != from))
            {
                yield return new WaitForSeconds(0.5f);
            }
            yield return new WaitForSeconds(2f);
            r.Info = $"ended {from} ({outcome}): {Where()}";
            if (D.preset == null || D.preset.name == from || !P.inEpilogue)
            {
                r.Fail($"didn't go on: {r.Info}");
            }
        }

        /// <summary>"epilogue-moved FROM" (host): the client's end of a part took us on too, out of FROM.</summary>
        private static IEnumerator Moved(string[] a, Result r)
        {
            string from = a.Length > 0 ? a[0] : Chapters.EpilogueDream;
            float until = Time.realtimeSinceStartup + 90f;
            while (Time.realtimeSinceStartup < until && !(D.dreaming && !D.switchingDream && D.preset != null && D.preset.name != from))
            {
                yield return new WaitForSeconds(0.5f);
            }
            yield return new WaitForSeconds(3f);
            r.Info = $"{Where()}; a friend's puppet shown {FriendShown()}";
            if (D.preset == null || D.preset.name == from || !P.inEpilogue)
            {
                r.Fail($"we stayed in {from}: {r.Info}");
            }
        }

        /// <summary>"epilogue-credits" (host): to the credits as EpilogueOutcomes.goToCredits does; the server stops.</summary>
        private static IEnumerator Credits(string[] a, Result r)
        {
            EpilogueSync.BeforeCredits();
            Chapters.ToCredits();
            while (SceneManager.GetActiveScene().name != "credits")
            {
                yield return new WaitForSeconds(0.5f);
            }
            r.Info = $"in the credits; session {Net.Session.Instance?.Active}";
            yield return ExitCredits();
        }

        /// <summary>"epilogue-menu" (client): the host's credits took us to ours, out of the session, then the menu.</summary>
        private static IEnumerator Menu(string[] a, Result r)
        {
            bool credits = false;
            while (!WorldShare.MainMenuReady())
            {
                if (!credits && SceneManager.GetActiveScene().name == "credits")
                {
                    credits = true;
                    yield return ExitCredits();
                }
                yield return new WaitForSeconds(0.5f);
            }
            r.Info = $"at the main menu (credits seen {credits}); session {Net.Session.Instance?.Active}";
            if (!credits)
            {
                r.Fail($"no credits: {r.Info}");
            }
            else if (Net.Session.Instance != null && Net.Session.Instance.Active)
            {
                r.Fail($"still in the session: {r.Info}");
            }
        }

        /// <summary>Where an outcome of the current part goes (the next part's preset name, or "").</summary>
        private static string Dest(DreamPreset.Outcome o)
        {
            if (o == null)
            {
                return "";
            }
            foreach (var e in o.effects)
            {
                if (e != null && e.type == DreamPreset.Outcome.Effect.Type.transferToDream && e.destPrefab != null)
                {
                    return e.destPrefab.name;
                }
            }
            return "";
        }

        /// <summary>
        /// "epilogue-onward PART" (client): end parts (the outcome that goes to PART if there is one, else the first)
        /// until we're in PART, at most 5 parts on. Each move is the shared dream's; "epilogue-at PART" checks the host.
        /// </summary>
        private static IEnumerator Onward(string[] a, Result r)
        {
            string want = a.Length > 0 ? a[0] : "crater";
            var path = new System.Collections.Generic.List<string>();
            for (int hop = 0; hop < 5 && !(D.preset != null && D.preset.name.Contains(want)); hop++)
            {
                string from = D.preset != null ? D.preset.name : "";
                if (D.preset == null || D.preset.outcomes.Count == 0)
                {
                    r.Fail($"no outcomes: {Where()}");
                    yield break;
                }
                var pick = D.preset.outcomes[0];
                foreach (var o in D.preset.outcomes)
                {
                    if (Dest(o).Contains(want))
                    {
                        pick = o;
                        break;
                    }
                }
                D.outcome = pick.name;
                path.Add($"{from} -{pick.name}-> {Dest(pick)}");
                AsPlayer(() => D.initiateEndDreaming());
                float until = Time.realtimeSinceStartup + 90f;
                while (Time.realtimeSinceStartup < until && !(D.dreaming && !D.switchingDream && D.preset != null && D.preset.name != from))
                {
                    yield return new WaitForSeconds(0.5f);
                }
                yield return new WaitForSeconds(4f);
            }
            r.Info = $"{string.Join("; ", path)}: {Where()}";
            if (D.preset == null || !D.preset.name.Contains(want) || !P.inEpilogue)
            {
                r.Fail($"didn't get to {want}: {r.Info}");
            }
        }

        /// <summary>"epilogue-at PART" (host): the client's ends took us to PART too.</summary>
        private static IEnumerator At(string[] a, Result r)
        {
            string want = a.Length > 0 ? a[0] : "crater";
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

        private static bool InPartLike(string name) => P != null && P.inEpilogue && D != null && D.dreaming && !D.switchingDream && D.preset != null && D.preset.name.Contains(name);

        /// <summary>"epilogue-burn" (host): our player sets the Being on fire (the crater's events run here).</summary>
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
            r.Info = $"{EpilogueSync.BurnEvents} fired {found.fired}; shared {EpilogueSync.BurnShared}";
            if (!EpilogueSync.BurnShared)
            {
                r.Fail($"not sent: {r.Info}");
            }
        }

        /// <summary>"epilogue-burned" (client): the host's burning of the Being ran in our crater too.</summary>
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
            while (Time.realtimeSinceStartup < until && !(found != null && found.fired))
            {
                yield return new WaitForSeconds(0.5f);
            }
            r.Info = $"{EpilogueSync.BurnEvents} found {found != null} fired {found != null && found.fired}; heard {EpilogueSync.BurnShared}";
            if (found == null || !found.fired)
            {
                r.Fail($"the Being didn't burn here: {r.Info}");
            }
        }

        /// <summary>
        /// "epilogue-crawl-die" (either): our player's first death in the fire starts the crawl, the second ends it
        /// (as the burning does). The pan must wait for the other player ("epilogue-pan").
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
                // A test shortcut: our player stands where the test put it, not where the burning leaves it, and the
                // CrawlStart clip didn't play here; end it as the clip's end does (Player's animation-completed handler).
                P.startingCrawling = false;
                P.performingAction = false;
                P.crawling = true;
            }
            if (!P.crawling)
            {
                r.Fail($"no crawl after the first death: startingCrawling {P.startingCrawling} dying {P.dying}");
                yield break;
            }
            yield return new WaitForSeconds(1f);
            P.die();
            yield return new WaitForSeconds(1f);
            r.Info = $"crawled {P.crawling} dying {P.dying}; our death told {EpilogueSync.OwnDeath}; pan released {EpilogueSync.PanReleased}";
            if (!EpilogueSync.OwnDeath)
            {
                r.Fail($"the second death wasn't the crawl's: {r.Info}");
            }
        }

        /// <summary>
        /// "epilogue-pan EARLY" (either): the pan ran here once every player had crawled to their death (not before:
        /// EARLY 1 checks it was still held 20 s after our own death); on the client the host's outcomes came too.
        /// </summary>
        private static IEnumerator PanStep(string[] a, Result r)
        {
            float until = Time.realtimeSinceStartup + 90f;
            while (Time.realtimeSinceStartup < until && !EpilogueSync.PanReleased)
            {
                yield return new WaitForSeconds(0.5f);
            }
            yield return new WaitForSeconds(12f);
            var outcomes = Object.FindObjectOfType<EpilogueOutcomes>();
            r.Info = $"pan released {EpilogueSync.PanReleased}; spectating {Spectator.Active}; host's burned {EpilogueSync.HostBurned}; outcomes screen {(outcomes != null ? (outcomes.burnedForest ? "burned" : "not burned") : "not shown")}";
            if (!EpilogueSync.PanReleased)
            {
                r.Fail($"no pan: {r.Info}");
            }
            else if (Net.Session.Instance.IsClient && EpilogueSync.HostBurned != 1)
            {
                r.Fail($"the host's outcomes didn't come: {r.Info}");
            }
            else if (outcomes != null && !outcomes.burnedForest)
            {
                r.Fail($"the outcomes aren't the burned forest's: {r.Info}");
            }
        }

        /// <summary>"epilogue-held" (host): our own crawl death alone doesn't run the pan (the client is still crawling).</summary>
        private static IEnumerator Held(string[] a, Result r)
        {
            yield return new WaitForSeconds(20f); // past the game's own 16 s
            r.Info = $"pan released {EpilogueSync.PanReleased}; spectating {Spectator.Active} (epilogue {Spectator.Epilogue})";
            if (EpilogueSync.PanReleased)
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
                AccessTools.Method(typeof(global::Credits), "exitToMainMenu").Invoke(c, null);
            }
            while (!WorldShare.MainMenuReady())
            {
                yield return new WaitForSeconds(0.5f);
            }
        }
    }
}
