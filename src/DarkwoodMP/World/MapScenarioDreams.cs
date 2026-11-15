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
            Add("dream-entered", DreamEntered, timeout: 90f);
            Add("home-door", HomeDoor, timeout: 10f);
            Add("home-knock", HomeKnock, timeout: 20f);
            Add("home-open", HomeOpen, timeout: 30f);
            Add("own-dream", OwnDream, timeout: 90f);
            Add("own-dream-use", OwnDreamUse, timeout: 60f);
        }

        /// <summary>"own-dream NAME" (client): go into the dream NAME by ourselves (not shared), as a story dream does.</summary>
        private static IEnumerator OwnDream(string[] a, Result r)
        {
            string name = a.Length > 0 ? a[0] : "dream_church_ruins_01";
            if (DreamSync.Shared != null) { r.Fail($"already in the shared dream {DreamSync.Shared}"); yield break; }
            DreamSync.TestOwnDream(name);
            float giveUp = Time.realtimeSinceStartup + 60f;
            while (!D.dreaming && Time.realtimeSinceStartup < giveUp) yield return new WaitForSeconds(0.5f);
            yield return new WaitForSeconds(3f); // the host's stand-in for us goes Away with our next snapshots
            r.Info = $"dreaming {D.preset?.name} (shared {DreamSync.Shared ?? "none"}) at {Locations.Current()}; host runs here {Locations.HostRunsHere()}";
            if (!D.dreaming || DreamSync.Shared != null || Locations.HostRunsHere()) r.Fail($"not in a dream of our own: {r.Info}");
        }

        /// <summary>
        /// "own-dream-use" (client, in our own church dream): the host hasn't got this dream, so what our player does
        /// in it is ours alone. We open and close the burnt wardrobe (a container, and the trigger that lets its
        /// chomper out), open a door if the dream has one, and send the host a request about the wardrobe as an old
        /// game would. Nothing may be refused or rolled back, the chomper comes out and runs in our game, the
        /// wardrobe and the door are still there, and the host takes the request without destroying our wardrobe.
        /// </summary>
        private static IEnumerator OwnDreamUse(string[] a, Result r)
        {
            var loc = D.dreamLocation;
            var et = loc != null ? loc.GetComponentsInChildren<EventTriggers>(true).FirstOrDefault(x => x.name.StartsWith("Wardrobe_Big_1_Burned")) : null;
            if (et == null) { r.Fail($"no burnt wardrobe in {loc?.name}"); yield break; }
            var item = et.GetComponent<Item>() ?? et.GetComponentInParent<Item>();
            int rolledBefore = Authority.RolledBack;
            yield return MoveNear(et.transform);

            string opened = "no container";
            if (item != null && item.GetComponent<Inventory>() != null)
            {
                try { AsPlayer(() => item.openInventory()); opened = $"opened (searched {item.searched})"; }
                catch (System.Exception e) { opened = "couldn't open: " + e.Message; }
                yield return new WaitForSeconds(1f); // Containers.Poll looks at it
                var inv = item.GetComponent<Inventory>();
                if (inv != null && inv.open) AsPlayer(() => inv.hide());
                yield return new WaitForSeconds(0.5f);
            }
            AsPlayer(() => et.fireEventTrigger(global::EventTrigger.Type.onCloseContainer));

            var ne = Combat.NetEnemies.Instance;
            float until = Time.realtimeSinceStartup + 15f;
            Character ours = null;
            while (ours == null && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.5f);
                ours = DreamCharacters().FirstOrDefault(c => c.name.StartsWith("ChomperBlack") && c.gameObject.activeInHierarchy && !ne.IsPuppet(c, out _));
            }

            var door = loc.GetComponentsInChildren<Door>(true).FirstOrDefault(d => d != null && d.gameObject.activeInHierarchy && !d.destroyed && !d.opened && !d.blocked
                && (d.GetComponent<Locked>() == null || !d.GetComponent<Locked>().locked));
            string doorInfo = "no door to open";
            if (door != null)
            {
                yield return MoveNear(door.transform);
                AsPlayer(() => door.open(P.transform.position, P.transform, 0f));
                yield return new WaitForSeconds(1f);
                doorInfo = door != null ? $"{door.name} open {door.opened}" : "the door is gone";
            }

            // As a game without the fix sent it: a request about our wardrobe, which the host hasn't got.
            bool? ok = null; string why = null;
            if (item != null)
            {
                var w = Net.Session.Instance.Begin(Net.MsgType.WorldAction);
                WorldActions.WriteAction(w, WorldActions.Action.ItemSearched, ObjRef.Of(item.gameObject), 0f, 0f, false, false, "");
                Authority.Request(w, $"forged ItemSearched {item.name} in our own dream", (o, reason, res) => { ok = o; why = reason; }, inventory: false);
                float giveUp = Time.realtimeSinceStartup + 8f;
                while (ok == null && Time.realtimeSinceStartup < giveUp) yield return null;
                yield return new WaitForSeconds(1f); // an ObjState after it, if any
            }

            int refused = Authority.RolledBack - rolledBefore;
            r.Info = $"in {loc?.name}: wardrobe {opened}, {(et != null && item != null ? "still there" : "gone")}; our chomper {(ours != null ? Fmt(ours.transform.position) + (Combat.NetEnemies.Frozen.Contains(ours) ? " (frozen)" : " running") : "not out")}; {doorInfo}; " +
                     $"forged request {(ok == null ? "no answer" : ok.Value ? "accepted" : "refused: " + why)}; refused {refused}";
            if (refused > 0) r.Fail($"the host refused what we did in our own dream: {r.Info}");
            else if (et == null || (item == null && opened != "no container")) r.Fail($"our wardrobe was destroyed: {r.Info}");
            else if (ours == null || Combat.NetEnemies.Frozen.Contains(ours)) r.Fail($"the wardrobe's event didn't run here: {r.Info}");
            else if (door != null && !door.opened) r.Fail($"the door didn't stay open: {r.Info}");
            else if (item != null && ok != true) r.Fail($"the host didn't take a request from our own dream: {r.Info}");
        }

        /// <summary>Our player next to t, on a spot with no loose thing our player would land on and push.</summary>
        private static IEnumerator MoveNear(Transform t)
        {
            var p = P;
            var away = p.transform.position - t.position; away.y = 0f;
            var dir = away.sqrMagnitude > 1f ? away.normalized : Vector3.right;
            var at = t.position + dir * 250f;
            for (int i = 0; i < 16; i++)
            {
                var spot = t.position + Quaternion.Euler(0f, 22.5f * i, 0f) * dir * 250f;
                spot.y = p.transform.position.y;
                if (Physics.OverlapSphere(spot, 100f).Any(col => col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic && col.GetComponentInParent<Player>() == null)) continue;
                at = spot;
                break;
            }
            p.transform.position = new Vector3(at.x, p.transform.position.y, at.z);
            var rb = p.GetComponent<Rigidbody>();
            if (rb != null) rb.position = p.transform.position;
            yield return new WaitForSeconds(0.5f);
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
            if (!D.allPresets.Any(x => x != null && x.name == name)) { r.Fail($"no dream {name}"); yield break; }
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

        /// <summary>
        /// "dream-entered" (any game, run in all at once with "host+client..."): once we're dreaming, when our game went
        /// into the dream ("entered T", Unix seconds); the driver's "max_spread" checks the games went in together.
        /// </summary>
        private static IEnumerator DreamEntered(string[] a, Result r)
        {
            while (!D.dreaming) yield return new WaitForSeconds(0.25f);
            r.Info = System.FormattableString.Invariant($"entered {DreamSync.EnteredAt:0.000}");
        }

        // The level dream at home (dream_home): the player wakes in the bedroom, and walking into the knocking area
        // (start_knocking_dreamHome) unlocks the bedroom's door (a modifyDoor unlock step) for the way out.
        private static EventTriggers Knocking() =>
            D.dreamLocation == null ? null : D.dreamLocation.GetComponentsInChildren<EventTriggers>(true).FirstOrDefault(x => x.name.StartsWith("start_knocking_dreamHome"));

        private static Door HomeDoorOf(EventTriggers et)
        {
            var ge = et == null ? null : et.GetComponent<GameEvents>();
            var step = ge == null ? null : ge.events.FirstOrDefault(e => e != null && e.type == GameEvent.Type.modifyDoor);
            var go = step?.targetGameObjects.FirstOrDefault(g => g != null);
            return go == null ? null : go.GetComponent<Door>() ?? go.GetComponentInChildren<Door>();
        }

        private static string DoorInfo(Door d)
        {
            var l = d.GetComponent<Locked>();
            return $"{d.name} at {Fmt(d.transform.position)} {ObjRef.Of(d.gameObject)} locked {(l != null && l.locked)} blocked {d.blocked} open {d.opened}";
        }

        /// <summary>"home-door locked|unlocked|open" (either): the bedroom door of the home dream is in that state here.</summary>
        private static IEnumerator HomeDoor(string[] a, Result r)
        {
            yield return new WaitForSeconds(0.5f);
            var door = HomeDoorOf(Knocking());
            if (door == null) { r.Fail($"no knocking area or door in {D.dreamLocation?.name}"); yield break; }
            var l = door.GetComponent<Locked>();
            bool locked = l != null && l.locked;
            r.Info = DoorInfo(door);
            string want = a.Length > 0 ? a[0] : "";
            if ((want == "locked" && !locked) || (want == "unlocked" && (locked || door.blocked)) || (want == "open" && !door.opened))
                r.Fail($"the door isn't {want}: {r.Info}");
        }

        /// <summary>"home-knock" (either): our player walks into the knocking area, which unlocks the bedroom door.</summary>
        private static IEnumerator HomeKnock(string[] a, Result r)
        {
            var et = Knocking();
            if (et == null) { r.Fail($"no knocking area in {D.dreamLocation?.name}"); yield break; }
            var p = P;
            p.transform.position = new Vector3(et.transform.position.x, p.transform.position.y, et.transform.position.z);
            var rb = p.GetComponent<Rigidbody>();
            if (rb != null) rb.position = p.transform.position;
            yield return new WaitForSeconds(0.3f);
            AsPlayer(() => et.fireEventTrigger(global::EventTrigger.Type.area));
            yield return new WaitForSeconds(3f);
            var door = HomeDoorOf(et);
            r.Info = $"walked into {et.name} at {Fmt(et.transform.position)}; " + (door != null ? DoorInfo(door) : "no door");
        }

        /// <summary>"home-open" (either): our player opens the bedroom door, as a player can only when it isn't locked here, and it stays open.</summary>
        private static IEnumerator HomeOpen(string[] a, Result r)
        {
            var door = HomeDoorOf(Knocking());
            if (door == null) { r.Fail($"no door in {D.dreamLocation?.name}"); yield break; }
            var p = P;
            var to = door.transform.position + (p.transform.position - door.transform.position).normalized * 150f;
            p.transform.position = new Vector3(to.x, p.transform.position.y, to.z);
            var rb = p.GetComponent<Rigidbody>();
            if (rb != null) rb.position = p.transform.position;
            yield return new WaitForSeconds(0.5f);
            var l = door.GetComponent<Locked>();
            if ((l != null && l.locked) || door.blocked) { r.Fail($"we're locked in: {DoorInfo(door)}"); yield break; }
            if (!door.opened) AsPlayer(() => door.open(p.transform.position, p.transform, 0f));
            yield return new WaitForSeconds(2f); // a refusal by the host closes it again
            r.Info = DoorInfo(door);
            if (!door.opened) r.Fail($"the door didn't stay open: {r.Info}");
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
