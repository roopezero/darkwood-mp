using System.Collections;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps for shared progression and the level dreams dreamt together (World.Progression,
    /// World.DreamSync). The list is tools/tests/map_scenario_dreams.py; they run after the dumps, since a dream
    /// moves the players, swaps their bags and sets the clock.
    /// </summary>
    public static class MapScenarioDreams
    {
        private static Player P => Player.Instance;
        private static Dreams D => Singleton<Dreams>.Instance;
        private static bool InDream => D != null && (D.dreaming || D.dreamPrepared || D.switchingDream || Core.EnteringDream);

        public static void Register()
        {
            Add("progress-shared", ProgressShared);
            Add("dream-clear", DreamClear, timeout: 90f);
            Add("level-dream", LevelDream, timeout: 90f);
            Add("in-host-dream", InHostDream, timeout: 90f);
            Add("end-level-dream", EndLevelDream, timeout: 90f);
            Add("back-from-dream", BackFromDream, timeout: 90f);
        }

        /// <summary>"progress-shared" (client): the host sent the shared essence and level, and our bar shows it up to our next level.</summary>
        private static IEnumerator ProgressShared(string[] a, Result r)
        {
            yield return new WaitForSeconds(1.5f);
            var p = P;
            var reqs = p.levelRequirements;
            int want = Mathf.Min(Progression.SharedExp, reqs[Mathf.Clamp(p.currentLevel, 0, reqs.Count - 1)]);
            r.Info = $"shared essence {Progression.SharedExp} level {Progression.SharedLevel}; ours {p.experience} at level {p.currentLevel}";
            if (p.experience != want) r.Fail($"our essence isn't the shared one up to our next level ({want}): {r.Info}");
        }

        /// <summary>"dream-clear" (either): out of any dream of our own (the night trader's, earlier), ending it if need be.</summary>
        private static IEnumerator DreamClear(string[] a, Result r)
        {
            float giveUp = Time.realtimeSinceStartup + 20f;
            while (InDream && Time.realtimeSinceStartup < giveUp) yield return new WaitForSeconds(0.5f);
            if (D.dreaming) { D.outcome = ""; D.initiateEndDreaming(); r.Info = "ended our own dream"; }
            while (InDream) yield return new WaitForSeconds(0.5f);
            yield return new WaitForSeconds(2f);
            r.Info = (r.Info ?? "not dreaming") + $"; at {P.transform.position.x:0} {P.transform.position.z:0}";
        }

        /// <summary>"level-dream NAME" (host): a level brings the dream NAME, as SkillsMenu.confirmSkills starts it; wait until we're in it.</summary>
        private static IEnumerator LevelDream(string[] a, Result r)
        {
            string name = a.Length > 0 ? a[0] : "dream_bunker_underground_01";
            DreamSync.TestLevelDream(name);
            while (!D.dreaming) yield return new WaitForSeconds(0.5f);
            yield return new WaitForSeconds(2f);
            r.Info = $"dreaming {D.preset?.name} (shared {DreamSync.Shared}) at {Locations.Current()}";
            if (DreamSync.Shared != name) r.Fail($"the dream isn't shared: {r.Info}");
        }

        /// <summary>"in-host-dream NAME" (client): we went into the host's dream by ourselves, and see the host there.</summary>
        private static IEnumerator InHostDream(string[] a, Result r)
        {
            string name = a.Length > 0 ? a[0] : "dream_bunker_underground_01";
            while (!D.dreaming) yield return new WaitForSeconds(0.5f);
            yield return new WaitForSeconds(3f);
            bool seen = false;
            var ps = Sync.PlayerSync.Instance;
            if (ps != null) foreach (var rp in ps.Remotes.Values) if (rp.Root != null && rp.Root.activeInHierarchy) seen = true;
            r.Info = $"dreaming {D.preset?.name} (shared {DreamSync.Shared}) at {Locations.Current()}; a friend's puppet shown {seen}";
            if (D.preset == null || D.preset.name != name || DreamSync.Shared != name) r.Fail($"not in the host's dream {name}: {r.Info}");
        }

        /// <summary>"end-level-dream" (host): our dream ends (its default outcome); wait until we're back.</summary>
        private static IEnumerator EndLevelDream(string[] a, Result r)
        {
            if (!D.dreaming) { r.Fail("not dreaming"); yield break; }
            D.outcome = "";
            AsPlayer(() => D.initiateEndDreaming());
            while (D.dreaming || D.switchingDream) yield return new WaitForSeconds(0.5f);
            yield return new WaitForSeconds(2f);
            r.Info = $"back at {P.transform.position.x:0} {P.transform.position.z:0}; shared dream {DreamSync.Shared ?? "none"}";
        }

        /// <summary>"back-from-dream" (client): the host's end ended ours.</summary>
        private static IEnumerator BackFromDream(string[] a, Result r)
        {
            while (D.dreaming || D.switchingDream) yield return new WaitForSeconds(0.5f);
            yield return new WaitForSeconds(2f);
            r.Info = $"back at {P.transform.position.x:0} {P.transform.position.z:0}; shared dream {DreamSync.Shared ?? "none"}";
            if (DreamSync.Shared != null) r.Fail($"still in a shared dream: {r.Info}");
        }
    }
}
