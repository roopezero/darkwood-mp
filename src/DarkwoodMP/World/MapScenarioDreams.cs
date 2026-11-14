using System.Collections;
using System.Linq;
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
            Add("dream-chars", DreamChars, timeout: 30f);
            Add("dream-wardrobe", DreamWardrobe, timeout: 30f);
            Add("dream-mob", DreamMobHost, timeout: 20f);
            Add("dream-mob-copies", DreamMobCopies, timeout: 10f);
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
            r.Info = (r.Info ?? "not dreaming") + $"; at {Fmt(P.transform.position)}";
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
            r.Info = $"back at {Fmt(P.transform.position)}; shared dream {DreamSync.Shared ?? "none"}";
        }

        /// <summary>"dream-chars" (either): the dream location's characters and events, logged (for reading a run).</summary>
        private static IEnumerator DreamChars(string[] a, Result r)
        {
            yield return new WaitForSeconds(a.Length > 0 ? float.Parse(a[0], System.Globalization.CultureInfo.InvariantCulture) : 1f);
            var loc = D.dreamLocation;
            if (loc == null) { r.Fail("no dream location"); yield break; }
            var ne = Combat.NetEnemies.Instance;
            var sb = new System.Text.StringBuilder();
            sb.Append($"dream {loc.name} at {Fmt(loc.transform.position)}, us at {Fmt(P.transform.position)}, hostRunsHere {Locations.HostRunsHere()}; ");
            foreach (var c in Object.FindObjectsOfType<Character>(true))
            {
                if (StateDump.Flat(c.transform.position - loc.transform.position) > 15000f) continue;
                uint id = 0; bool net = ne != null && (ne.NetIdOf(c, out id) || ne.IsPuppet(c, out id));
                sb.Append($"\n  {c.name} npc {(c.npc != null)} dummy {c.dummy} elig {Combat.NetEnemies.Eligible(c)} act {c.gameObject.activeInHierarchy} en {c.enabled} alive {c.alive} " +
                          $"at {Fmt(c.transform.position)} tgt {(c.target != null ? c.target.name : "-")} net {net}#{id} aggr {c.aggressive} sleep {c.sleeping} " +
                          $"comps {string.Join(",", c.GetComponents<MonoBehaviour>().Select(m => m == null ? "?" : m.GetType().Name))}");
            }
            foreach (var ge in loc.GetComponentsInChildren<GameEvents>(true)) sb.Append($"\n  events {ge.name} act {ge.gameObject.activeInHierarchy}");
            r.Info = sb.ToString();
            Plugin.Log.LogInfo("[dream-mob] " + r.Info);
        }

        /// <summary>The dream location's characters (within a location's reach of its marker), active or not.</summary>
        private static System.Collections.Generic.IEnumerable<Character> DreamCharacters() =>
            Object.FindObjectsOfType<Character>(true).Where(c => D.dreamLocation != null && Combat.NetEnemies.Eligible(c)
                && StateDump.Flat(c.transform.position - D.dreamLocation.transform.position) < 15000f);

        /// <summary>
        /// "dream-wardrobe" (client): in the church dream, our player closes the burnt wardrobe, which lets its
        /// chomper out (spawnChomper_dream_church_ruins_01). The host runs the event and the chomper; we wait until
        /// the host's chomper is shown here.
        /// </summary>
        private static IEnumerator DreamWardrobe(string[] a, Result r)
        {
            var loc = D.dreamLocation;
            var et = loc != null ? loc.GetComponentsInChildren<EventTriggers>(true).FirstOrDefault(x => x.name.StartsWith("Wardrobe_Big_1_Burned")) : null;
            if (et == null) { r.Fail($"no burnt wardrobe in {loc?.name}"); yield break; }
            var p = P;
            // Near it, on a spot with no loose thing (a bowl, the draggable wardrobe) our player would land on and push.
            var away = p.transform.position - et.transform.position; away.y = 0f;
            var dir = away.sqrMagnitude > 1f ? away.normalized : Vector3.right;
            var at = et.transform.position + dir * 250f;
            for (int i = 0; i < 16; i++)
            {
                var spot = et.transform.position + Quaternion.Euler(0f, 22.5f * i, 0f) * dir * 250f;
                spot.y = p.transform.position.y;
                if (Physics.OverlapSphere(spot, 100f).Any(col => col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic && col.GetComponentInParent<Player>() == null)) continue;
                at = spot;
                break;
            }
            p.transform.position = new Vector3(at.x, p.transform.position.y, at.z);
            var rb = p.GetComponent<Rigidbody>();
            if (rb != null) rb.position = p.transform.position;
            yield return new WaitForSeconds(0.5f);
            AsPlayer(() => et.fireEventTrigger(global::EventTrigger.Type.onCloseContainer));
            var ne = Combat.NetEnemies.Instance;
            float until = Time.realtimeSinceStartup + 15f;
            Character shown = null;
            while (shown == null && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.5f);
                shown = DreamCharacters().FirstOrDefault(c => c.name.StartsWith("ChomperBlack") && c.gameObject.activeInHierarchy && ne.IsPuppet(c, out _));
            }
            r.Info = $"closed {et.name} at {Fmt(et.transform.position)} from {Fmt(p.transform.position)}; the host's chomper shown: {(shown != null ? Fmt(shown.transform.position) : "no")}";
            if (shown == null) r.Fail($"the host's chomper isn't shown here: {r.Info}");
        }

        /// <summary>
        /// "dream-mob" (host): the dream's chomper is out and running in our game, sent to the client (a net id), and
        /// goes for someone by threat; it bites for 1, so the comparison isn't cut short by a death.
        /// </summary>
        private static IEnumerator DreamMobHost(string[] a, Result r)
        {
            var ne = Combat.NetEnemies.Instance;
            Character c = null;
            float until = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < until)
            {
                c = DreamCharacters().FirstOrDefault(x => x.name.StartsWith("ChomperBlack") && x.alive && x.gameObject.activeInHierarchy && ne.NetIdOf(x, out _));
                if (c != null && c.target != null) break;
                yield return new WaitForSeconds(0.25f);
            }
            if (c == null) { r.Fail("no chomper of the dream running here"); yield break; }
            foreach (var s in c.sensorTypes) if (s != null) s.damage = 1;
            var proxy = Combat.NetPlayerProxy.All.Values.FirstOrDefault(x => x != null && x.Owner != Sync.PlayerSync.DummyId);
            ne.NetIdOf(c, out uint id);
            r.Info = $"{c.name} #{id} at {Fmt(c.transform.position)}, target {(c.target != null ? c.target.name : "none")}; " +
                     $"the client's stand-in: away {proxy?.Away}, in {(proxy?.InLocation != null ? proxy.InLocation.name : "-")}" +
                     (proxy != null ? $", threat {Combat.Threat.Of(c).Get(proxy):0.#}" : "");
            if (proxy == null || proxy.Away || proxy.InLocation != D.dreamLocation) r.Fail($"the client isn't in the dream we run: {r.Info}");
            else if (c.target == null) r.Fail($"the chomper goes for nobody: {r.Info}");
        }

        /// <summary>"dream-mob-copies" (client): none of our own dream characters runs; the host's are the ones shown.</summary>
        private static IEnumerator DreamMobCopies(string[] a, Result r)
        {
            yield return new WaitForSeconds(0.5f);
            var ne = Combat.NetEnemies.Instance;
            var all = DreamCharacters().ToList();
            int puppets = all.Count(c => ne.IsPuppet(c, out _) && c.gameObject.activeInHierarchy);
            var running = all.Where(c => !ne.IsPuppet(c, out _) && c.gameObject.activeInHierarchy && !Combat.NetEnemies.Frozen.Contains(c)).ToList();
            r.Info = $"host runs it here {Locations.HostRunsHere()}; the host's shown {puppets}; ours running {running.Count}" +
                     (running.Count > 0 ? $" ({string.Join(", ", running.Select(c => c.name))})" : "");
            if (!Locations.HostRunsHere() || puppets == 0 || running.Count > 0) r.Fail($"the dream's characters aren't the host's: {r.Info}");
        }

        /// <summary>"back-from-dream" (client): the host's end ended ours.</summary>
        private static IEnumerator BackFromDream(string[] a, Result r)
        {
            while (D.dreaming || D.switchingDream) yield return new WaitForSeconds(0.5f);
            yield return new WaitForSeconds(2f);
            r.Info = $"back at {Fmt(P.transform.position)}; shared dream {DreamSync.Shared ?? "none"}";
            if (DreamSync.Shared != null) r.Fail($"still in a shared dream: {r.Info}");
        }
    }
}
