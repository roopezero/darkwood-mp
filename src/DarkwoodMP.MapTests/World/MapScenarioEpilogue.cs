using DarkwoodMP.World.Dreaming;
using DarkwoodMP.World.Story;
using System.Collections;
using UnityEngine.SceneManagement;
using UnityEngine;
using Chapters = DarkwoodMP.World.Story.Chapters;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps for the epilogue played together
    /// (World.Story.EpilogueSync). The list is
    /// tools/tests/map_scenario_epilogue.py; the epilogue ends the game, so it
    /// runs alone and last.
    /// </summary>
    public static partial class MapScenarioEpilogue
    {
        /// Whether our player is watching the others.
        private static bool Spectating()
        {
            CoopSession coop = Coop.Current;
            return coop != null && coop.Spectator.Active;
        }

        /// Whether our player watches the others in the epilogue.
        private static bool WatchingEpilogue()
        {
            CoopSession coop = Coop.Current;
            return coop != null && coop.Spectator.Epilogue;
        }

        /// Whether the shared epilogue is on.
        private static bool Playing()
        {
            CoopSession coop = Coop.Current;
            return coop != null && coop.Epilogue.Playing;
        }

        /// Whether a player's fire was shared.
        private static bool BurnShared()
        {
            CoopSession coop = Coop.Current;
            return coop != null && coop.Epilogue.BurnShared;
        }

        /// Whether our player's crawl to their death was counted.
        private static bool OwnDeath()
        {
            CoopSession coop = Coop.Current;
            return coop != null && coop.Epilogue.OwnDeath;
        }

        /// Whether the pan over the burning forest was released.
        private static bool PanReleased()
        {
            CoopSession coop = Coop.Current;
            return coop != null && coop.Epilogue.PanReleased;
        }

        /// The host's outcomes as this joiner heard them.
        private static HostBurned HeardBurned()
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return HostBurned.Unheard;
            }

            return coop.Epilogue.HostBurned;
        }

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
            RegisterStart();
            Add("epilogue-tags", Tags, timeout: 30f);
            RegisterDialog();
            Add("epiapt-dump", AptDump, timeout: 30f);
            Add("epiapt-clothes", AptClothes, timeout: 30f);
            Add("epiapt-puppet-clothes", AptPuppetClothes, timeout: 30f);
            Add("epiapt-room", AptRoom, timeout: 60f);
            Add("epiapt-pet", AptPet, timeout: 60f);
            Add("epiapt-wardrobe", AptWardrobe, timeout: 30f);
            Add("epiapt-wall-gone", AptWallGone, timeout: 30f);
            Add("epiapt-bed-ready", AptBedReady, timeout: 30f);
            Add("epiapt-bed-moved", AptBedMoved, timeout: 30f);
            Add("epiapt-floor-ready", AptFloorReady, timeout: 30f);
            Add("epiapt-floor", AptFloor, timeout: 40f);
            Add("epiapt-floor-broken", AptFloorBroken, timeout: 30f);
            Add("epilogue-layout", Layout, timeout: 120f);
            Add("epilogue-flag", SetFlag, timeout: 30f);
            Add("epilogue-events", RunEvents, timeout: 30f);
            Add("epilogue-outcomes", Outcomes, timeout: 300f);
            Add("epilogue-menu-works", MenuWorks, timeout: 200f);
            Add("epilogue-drop", Drop, timeout: 30f);
        }

        private static string Where()
        {
            Dreams d = D;
            string name = null;
            if (d != null)
            {
                DreamPreset preset = d.preset;
                if (preset != null)
                {
                    name = preset.name;
                }
            }

            if (name == null)
            {
                name = "none";
            }

            bool dreaming = D != null && D.dreaming;
            bool inEpilogue = P != null && P.inEpilogue;
            string shared = Coop.Current.Dreaming.Shared;
            if (shared == null)
            {
                shared = "none";
            }

            return $"preset {name} dreaming {dreaming} inEpilogue {inEpilogue} "
                + $"shared {shared} playing {Playing()}";
        }

        private static bool InEntered(string[] a)
        {
            if (a.Length > 0)
            {
                return InPartLike(a[0]);
            }

            return InPart(Chapters.EpilogueDream);
        }

        private static bool InPart(string name) => P != null && P.inEpilogue
            && D != null && D.dreaming && !D.switchingDream
            && D.preset != null && D.preset.name == name;

        private static bool FriendShown()
        {
            var ps = Plugin.Instance.Host.Players;
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

        /// <summary>
        /// "epilogue-start" (host): the game's last step starts the epilogue;
        /// wait until we're in its first part.
        /// </summary>
        private static IEnumerator Start(string[] a, Result r)
        {
            GameEvent step = Chapters.StepFor(StoryKind.Epilogue, 0);
            Singleton<Controller>.Instance.StartCoroutine(
                step.fire(P.gameObject));

            while (!InPart(Chapters.EpilogueDream))
            {
                yield return new WaitForSeconds(0.5f);
            }

            yield return new WaitForSeconds(2f);
            r.Info = Where();
            if (!Playing() || Coop.Current.Dreaming.Shared == null)
            {
                r.Fail($"the epilogue isn't shared: {r.Info}");
            }
        }

        /// <summary>
        /// "epilogue-in [PART]" (client): we went into the host's epilogue by
        /// ourselves, still connected: its first part, or with PART the part
        /// whose name has it (a late join).
        /// </summary>
        private static IEnumerator In(string[] a, Result r)
        {
            while (!InEntered(a))
            {
                yield return new WaitForSeconds(0.5f);
            }

            yield return new WaitForSeconds(4f);
            string state = Where();
            bool? active = null;
            Net.Session session = Plugin.Instance.Host.Session;
            if (session != null)
            {
                active = session.Active;
            }

            r.Info =
                $"{state}; session {active}; a friend's puppet shown "
                    + $"{FriendShown()}";
            if (!Playing() || Coop.Current.Dreaming.Shared == null
                || !Plugin.Instance.Host.Session.Active)
            {
                r.Fail($"not in the shared epilogue: {r.Info}");
            }
        }

        /// <summary>
        /// "epilogue-next" (client): our part ends (its first outcome, as its
        /// endDream step does); we go on to the next.
        /// </summary>
        private static IEnumerator Next(string[] a, Result r)
        {
            string from = "";
            if (D.preset != null)
            {
                from = D.preset.name;
            }

            if (D.preset == null || D.preset.outcomes.Count == 0)
            {
                r.Fail($"no outcomes: {Where()}");
                yield break;
            }

            D.outcome = D.preset.outcomes[0].name;
            string outcome = D.outcome;
            AsPlayer(() => D.initiateEndDreaming());
            float until = Time.realtimeSinceStartup + 90f;
            while (Time.realtimeSinceStartup < until
                && !(D.dreaming && !D.switchingDream && D.preset != null
                    && D.preset.name != from))
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

        /// <summary>
        /// "epilogue-moved FROM" (host): the client's end of a part took us on
        /// too, out of FROM.
        /// </summary>
        private static IEnumerator Moved(string[] a, Result r)
        {
            string from;
            if (a.Length > 0)
            {
                from = a[0];
            }
            else
            {
                from = Chapters.EpilogueDream;
            }

            float until = Time.realtimeSinceStartup + 90f;
            while (Time.realtimeSinceStartup < until
                && !(D.dreaming && !D.switchingDream && D.preset != null
                    && D.preset.name != from))
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

        /// <summary>
        /// "epilogue-credits" (host): to the credits as
        /// EpilogueOutcomes.goToCredits does; the server stops.
        /// </summary>
        private static IEnumerator Credits(string[] a, Result r)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Epilogue.BeforeCredits();
            }

            Chapters.ToCredits();
            while (SceneManager.GetActiveScene().name != "credits")
            {
                yield return new WaitForSeconds(0.5f);
            }

            Net.Session instance = Plugin.Instance.Host.Session;
            bool? active = null;
            if (instance != null)
            {
                active = instance.Active;
            }

            r.Info = $"in the credits; session {active}";
            yield return ExitCredits();
        }

        /// <summary>
        /// "epilogue-menu" (client): the host's credits took us to ours, out of
        /// the session, then the menu.
        /// </summary>
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

            Net.Session instance = Plugin.Instance.Host.Session;
            bool? active = null;
            if (instance != null)
            {
                active = instance.Active;
            }

            r.Info =
                $"at the main menu (credits seen {credits}); session {active}";
            if (!credits)
            {
                r.Fail($"no credits: {r.Info}");
            }
            else if (Plugin.Instance.Host.Session.Active)
            {
                r.Fail($"still in the session: {r.Info}");
            }
        }

        /// <summary>
        /// Where an outcome of the current part goes (the next part's preset
        /// name, or "").
        /// </summary>
        private static string Dest(DreamPreset.Outcome o)
        {
            if (o == null)
            {
                return "";
            }

            foreach (var e in o.effects)
            {
                if (e != null
                    && e.type
                        == DreamPreset.Outcome.Effect.Type.transferToDream
                    && e.destPrefab != null)
                {
                    return e.destPrefab.name;
                }
            }

            return "";
        }

        /// <summary>
        /// "epilogue-onward PART" (client): end parts (the outcome that goes to
        /// PART if there is one, else the first) until we're in PART, at most 5
        /// parts on. Each move is the shared dream's; "epilogue-at PART" checks
        /// the host.
        /// </summary>
        private static IEnumerator Onward(string[] a, Result r)
        {
            string want = "crater";
            if (a.Length > 0)
            {
                want = a[0];
            }

            var path = new System.Collections.Generic.List<string>();
            for (int hop = 0; hop < 5
                && !(D.preset != null && D.preset.name.Contains(want)); hop++)
            {
                string from = "";
                if (D.preset != null)
                {
                    from = D.preset.name;
                }

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
                while (Time.realtimeSinceStartup < until
                    && !(D.dreaming && !D.switchingDream && D.preset != null
                        && D.preset.name != from))
                {
                    yield return new WaitForSeconds(0.5f);
                }

                yield return new WaitForSeconds(4f);
            }

            r.Info = $"{string.Join("; ", path)}: {Where()}";
            if (D.preset == null || !D.preset.name.Contains(want)
                || !P.inEpilogue)
            {
                r.Fail($"didn't get to {want}: {r.Info}");
            }
        }
    }
}
