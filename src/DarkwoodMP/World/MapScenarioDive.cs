using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>For MapScenarioDive: put on the player; a runFunction step on the player calls this (SendMessage with the trigger's ID).</summary>
    public sealed class PlayerStepCounter : MonoBehaviour
    {
        public readonly Dictionary<string, int> N = new Dictionary<string, int>();
        private void DwmpOwnCount(string id) => N[id] = Of(id) + 1;
        public int Of(string id)
        {
            if (N.TryGetValue(id, out var n))
            {
                return n;
            }
            return 0;
        }
    }

    /// <summary>
    /// Steps of the two-game map test for diving in the village cellar and steps that act on the player
    /// (tools/tests/map_scenario_dive.py).
    ///
    /// A step whose target is the player (the unique object "player", or the player's own object) was run as a shared
    /// step, so each game ran it on its own player: one player diving in the cellar (runFunction diveIn on "player")
    /// made every player dive. Now it's the player's own (ScriptedEvents.TargetsPlayer), and a later player of a
    /// once-only trigger gets it too.
    /// </summary>
    public static class MapScenarioDive
    {
        public static void Register()
        {
            Add("dive-scan", Scan, timeout: 30f);
            Add("own-setup", OwnSetup);
            Add("own-fire", OwnFire, timeout: 30f);
            Add("own-count", OwnCount, timeout: 15f);
            Add("trip-setup", TripSetup);
            Add("trip-go", TripGo, timeout: 40f);
            Add("tank-setup", TankSetup);
            Add("tank-give", TankGive, timeout: 15f);
            Add("tank-take", TankTake);
            Add("tank-flag", TankFlag, timeout: 15f);
            Add("tank-fire", TankFire, timeout: 30f);
        }

        // ---- the full oxygen tank, once filled, for every player (SharedTank) ----------------------------------

        private static readonly Dictionary<string, (EventTriggers et, Vector3 pos)> _tanks = new Dictionary<string, (EventTriggers, Vector3)>();

        private static GameObject TankPrefab()
        {
            var db = Singleton<ItemsDatabase>.Instance;
            if (db != null && db.itemsDict.TryGetValue(SharedTank.Item, out var path))
            {
                return Resources.Load(path) as GameObject;
            }
            return null;
        }

        /// <summary>
        /// "tank-setup ID DX DZ" (both games): an onActivate thing ID at the centre plus (DX, DZ) made as the cellar's
        /// passages are: a trigger that needs the full tank (playerState/haveItem oxygenTank_full) and counts "ID" on our
        /// player, and one that needs not having it and counts "ID-no".
        /// </summary>
        private static IEnumerator TankSetup(string[] a, Result r)
        {
            if (a.Length < 3)
            {
                r.Fail("tank-setup ID DX DZ");
                yield break;
            }
            var prefab = TankPrefab();
            if (prefab == null)
            {
                r.Fail($"no item {SharedTank.Item}");
                yield break;
            }
            string id = a[0];
            var pos = Centre + new Vector3(F(a[1]), 0f, F(a[2]));
            var go = Root("dwmp_tank_" + id, pos);
            var et = go.AddComponent<EventTriggers>();
            foreach (var (has, counted) in new[] { (true, id), (false, id + "-no") })
            {
                var g = new GameObject("dwmp_tank_events_" + counted);
                g.transform.SetParent(go.transform, false);
                var ge = g.AddComponent<GameEvents>();
                ge.multipleFire = true;
                ge.events.Add(new GameEvent { type = GameEvent.Type.runFunction, Value = "DwmpOwnCount", activeModifier2 = true, stringValue = counted, targetUniqueObjects = new List<string> { "player" }, eventAmount = 1 });
                var t = new global::EventTrigger { type = global::EventTrigger.Type.onActivate, multipleFire = true, gameEvents = ge };
                t.eventRequirements.Add(new EventTriggerRequirement { type = EventTriggerRequirement.Type.playerState, playerState = Player.State.haveItem, itemType = prefab, activeModifier = has });
                et.eventTriggers.Add(t);
                t.thisEventTriggers = et;
            }
            Counter().N.Remove(id);
            Counter().N.Remove(id + "-no");
            _tanks[id] = (et, pos);
            yield return null;
            var inv = prefab.GetComponent<InvItem>();
            r.Info = $"{id} {ObjRef.Of(go)} at {Fmt(pos)}; {SharedTank.Item}: important item {inv.isImportantItem}, stackable {inv.stackable}; filled {SharedTank.Filled}";
        }

        /// <summary>"tank-give": our player gets a full tank, and our game must set the flag.</summary>
        private static IEnumerator TankGive(string[] a, Result r)
        {
            // Room in the bags first (a full inventory drops it on the ground instead).
            var bag = Player.Instance.Inventory;
            if (!bag.slots.Exists(s => s != null && InvItemClass.isNull(s.invItem)))
            {
                var last = bag.slots.FindLast(s => s != null && !InvItemClass.isNull(s.invItem));
                if (last != null)
                {
                    bag.removeItemAmountFromPlayer(last.invItem.type, last.invItem.amount);
                }
            }
            bag.addItemTypeToPlayer(SharedTank.Item, 1, dropIfNoRoom: true);
            float until = Time.realtimeSinceStartup + 8f;
            while (!SharedTank.Filled && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.2f);
            }
            r.Info = $"carried {MapScenarioWorld.CarriedOf(SharedTank.Item)}, filled {SharedTank.Filled}";
            if (!SharedTank.Filled)
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>"tank-take": our player's full tanks are taken away (the flag stays).</summary>
        private static IEnumerator TankTake(string[] a, Result r)
        {
            int n = MapScenarioWorld.CarriedOf(SharedTank.Item);
            Player.Instance.Inventory.removeItemAmountFromPlayer(SharedTank.Item, n, includeAdditionalInventory: true);
            yield return null;
            r.Info = $"took {n}, carried {MapScenarioWorld.CarriedOf(SharedTank.Item)}, filled {SharedTank.Filled}";
            if (MapScenarioWorld.CarriedOf(SharedTank.Item) != 0)
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>"tank-flag 1|0": the flag is set here (it came from the other game), and is in what a save writes.</summary>
        private static IEnumerator TankFlag(string[] a, Result r)
        {
            bool want = a.Length > 0 && a[0] == "1";
            float until = Time.realtimeSinceStartup + 8f;
            while (SharedTank.Filled != want && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.2f);
            }
            var saved = new Flags.SaveState(Singleton<Flags>.Instance).flags.Exists(x => x != null && x.name == SharedTank.Flag && x.isTrue);
            r.Info = $"filled {SharedTank.Filled} (want {want}), in the save {saved}";
            if (SharedTank.Filled != want || saved != want)
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>"tank-fire ID YES NO": our player uses ID; its tank trigger must have run YES times for us in all, the no-tank one NO.</summary>
        private static IEnumerator TankFire(string[] a, Result r)
        {
            if (a.Length < 3 || !_tanks.TryGetValue(a[0], out var m) || m.et == null)
            {
                r.Fail("tank-fire ID YES NO (tank-setup first)");
                yield break;
            }
            int yes = int.Parse(a[1]), no = int.Parse(a[2]);
            yield return Walk(m.pos, 100f);
            yield return new WaitForSeconds(0.5f);
            AsPlayer(() => m.et.fireEventTrigger(global::EventTrigger.Type.onActivate));
            var c = Counter();
            float until = Time.realtimeSinceStartup + 6f;
            while (Time.realtimeSinceStartup < until && c.Of(a[0]) + c.Of(a[0] + "-no") < yes + no)
            {
                yield return new WaitForSeconds(0.1f);
            }
            yield return new WaitForSeconds(1f);
            r.Info = $"with the tank ran {c.Of(a[0])} (want {yes}), without it {c.Of(a[0] + "-no")} (want {no}); carried {MapScenarioWorld.CarriedOf(SharedTank.Item)}, filled {SharedTank.Filled}";
            if (c.Of(a[0]) != yes || c.Of(a[0] + "-no") != no)
            {
                r.Fail(r.Info);
            }
        }

        // ---- a short scripted trip, which the host's movement check must let through ---------------------------

        private sealed class Trip
        {
            public EventTriggers Et;
            public Transform To;
        }
        private static readonly Dictionary<string, Trip> _trips = new Dictionary<string, Trip>();

        /// <summary>
        /// "trip-setup ID DX DZ DX2 DZ2" (both games): an area trigger ID at the centre plus (DX, DZ), each time, whose
        /// one step takes the player to a marker at the centre plus (DX2, DZ2) (transportPlayerToObject, as the
        /// cellar's dive does).
        /// </summary>
        private static IEnumerator TripSetup(string[] a, Result r)
        {
            if (a.Length < 5)
            {
                r.Fail("trip-setup ID DX DZ DX2 DZ2");
                yield break;
            }
            var pos = Centre + new Vector3(F(a[1]), 0f, F(a[2]));
            var go = Root("dwmp_trip_" + a[0], pos);
            var to = Root("dwmp_trip_to_" + a[0], Centre + new Vector3(F(a[3]), 0f, F(a[4]))).transform;
            var ge = go.AddComponent<GameEvents>();
            ge.multipleFire = true;
            ge.events.Add(new GameEvent { type = GameEvent.Type.transportPlayerToObject, targetGameObjects = new List<GameObject> { to.gameObject }, floatValue = 0.1f, magnitude = 0.1f, eventAmount = 1 });
            var et = go.AddComponent<EventTriggers>();
            var t = new global::EventTrigger { type = global::EventTrigger.Type.area, multipleFire = true, gameEvents = ge };
            et.eventTriggers.Add(t);
            t.thisEventTriggers = et;
            _trips[a[0]] = new Trip { Et = et, To = to };
            yield return null;
            r.Info = $"trip {a[0]} {ObjRef.Of(go)} from {Fmt(pos)} to {Fmt(to.position)}, {StateDump.Flat(to.position - pos):0} away";
        }

        /// <summary>
        /// "trip-go ID": our player sets off trip ID, arrives at its marker and must stay there: the host may not put
        /// it back (PlayerCorrection) as if it had moved too fast.
        /// </summary>
        private static IEnumerator TripGo(string[] a, Result r)
        {
            if (a.Length < 1 || !_trips.TryGetValue(a[0], out var m) || m.Et == null)
            {
                r.Fail("trip-go ID (trip-setup first)");
                yield break;
            }
            yield return Walk(m.Et.transform.position, 100f);
            yield return new WaitForSeconds(1f);
            int corrections = Sync.MoveCheck.Corrections;
            var p = Player.Instance;
            bool There() => StateDump.Flat(p.transform.position - m.To.position) < 150f;
            AsPlayer(() => m.Et.fireEventTrigger(global::EventTrigger.Type.area));
            m.Et.entered++;
            float until = Time.realtimeSinceStartup + 10f;
            while (!There() && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
            bool arrived = There();
            yield return new WaitForSeconds(3f); // a correction comes within a few State updates
            r.Info = $"arrived {arrived}, now at {Fmt(p.transform.position)} ({StateDump.Flat(p.transform.position - m.To.position):0} from the marker), " +
                     $"put back {Sync.MoveCheck.Corrections - corrections} time(s)";
            if (!arrived)
            {
                r.Fail($"the trip didn't take us there: {r.Info}");
            }
            else if (!There() || Sync.MoveCheck.Corrections != corrections)
            {
                r.Fail($"the host put us back: {r.Info}");
            }
            Core.forbidInputs = false;
            yield return Walk(Centre); // a jump back would be a speed hack to the host's movement check
            yield return new WaitForSeconds(1f);
        }

        private sealed class Made
        {
            public EventTriggers Et;
            public global::EventTrigger T;
            public LaterTestCounter C;
        }
        private static readonly Dictionary<string, Made> _made = new Dictionary<string, Made>();

        private static PlayerStepCounter Counter()
        {
            var p = Player.Instance;
            PlayerStepCounter component = p.GetComponent<PlayerStepCounter>();
            if (component == null)
            {
                component = p.gameObject.AddComponent<PlayerStepCounter>();
            }
            return component;
        }

        private static GameObject Root(string name, Vector3 pos)
        {
            var old = GameObject.Find(name);
            if (old != null)
            {
                Object.Destroy(old);
            }
            var go = new GameObject(name);
            go.transform.position = pos;
            return go;
        }

        /// <summary>
        /// "own-setup ID DX DZ once|multi|eventsonce" (both games): an area trigger ID at the centre plus (DX, DZ) with three
        /// runFunction steps: one on the unique object "player", one on our player's own object (each counts on our
        /// player, by ID), and a shared one on a counter beside it.
        /// </summary>
        private static IEnumerator OwnSetup(string[] a, Result r)
        {
            if (a.Length < 4)
            {
                r.Fail("own-setup ID DX DZ once|multi|eventsonce");
                yield break;
            }
            string id = a[0];
            var pos = Centre + new Vector3(F(a[1]), 0f, F(a[2]));
            bool multi = a[3] != "once"; // eventsonce: the trigger fires each time, its GameEvents run once (the cellar's first dive)
            var m = new Made();
            var go = Root("dwmp_own_" + id, pos);
            var ge = go.AddComponent<GameEvents>();
            ge.multipleFire = a[3] == "multi";
            var counter = Counter();
            counter.N.Remove(id);
            m.C = Root("dwmp_own_count_" + id, pos).AddComponent<LaterTestCounter>();
            ge.events.Add(new GameEvent { type = GameEvent.Type.runFunction, Value = "DwmpOwnCount", activeModifier2 = true, stringValue = id, targetUniqueObjects = new List<string> { "player" }, eventAmount = 1 });
            ge.events.Add(new GameEvent { type = GameEvent.Type.runFunction, Value = "DwmpOwnCount", activeModifier2 = true, stringValue = id, targetGameObjects = new List<GameObject> { Player.Instance.gameObject }, eventAmount = 1 });
            ge.events.Add(new GameEvent { type = GameEvent.Type.runFunction, Value = "DwmpLaterCount", targetGameObjects = new List<GameObject> { m.C.gameObject }, eventAmount = 1 });
            m.Et = go.AddComponent<EventTriggers>();
            m.T = new global::EventTrigger { type = global::EventTrigger.Type.area, multipleFire = multi, gameEvents = ge };
            m.Et.eventTriggers.Add(m.T);
            m.T.thisEventTriggers = m.Et;
            _made[id] = m;
            yield return null;
            r.Info = $"trigger {id} {ObjRef.Of(go)} at {Fmt(pos)}, {a[3]}; the unique object player is ours: {Singleton<UniqueObjects>.Instance.getObject("player") == Player.Instance.gameObject}";
        }

        /// <summary>"own-fire ID OWN": our player walks to trigger ID and sets it off; OWN steps must act on our player (2, or 0).</summary>
        private static IEnumerator OwnFire(string[] a, Result r)
        {
            if (a.Length < 2 || !_made.TryGetValue(a[0], out var m) || m.Et == null)
            {
                r.Fail("own-fire ID OWN (own-setup first)");
                yield break;
            }
            int want = int.Parse(a[1]);
            yield return Walk(m.Et.transform.position, 100f);
            yield return new WaitForSeconds(0.5f);
            var c = Counter();
            int had = c.Of(a[0]);
            AsPlayer(() => m.Et.fireEventTrigger(global::EventTrigger.Type.area));
            m.Et.entered++;
            float until = Time.realtimeSinceStartup + 6f;
            while (Time.realtimeSinceStartup < until && c.Of(a[0]) - had < want)
            {
                yield return new WaitForSeconds(0.1f);
            }
            yield return new WaitForSeconds(1f);
            int got = c.Of(a[0]) - had;
            r.Info = $"{got} step(s) acted on our player (want {want}), the shared step ran {m.C.N} time(s) here";
            if (got != want)
            {
                r.Fail(r.Info);
            }
            MapScenario.Place(Centre);
        }

        /// <summary>"own-count ID OWN SHARED": in this game, OWN steps of ID acted on our player and its shared step ran SHARED times.</summary>
        private static IEnumerator OwnCount(string[] a, Result r)
        {
            if (a.Length < 3 || !_made.TryGetValue(a[0], out var m) || m.C == null)
            {
                r.Fail("own-count ID OWN SHARED (own-setup first)");
                yield break;
            }
            int own = int.Parse(a[1]), shared = int.Parse(a[2]);
            float until = Time.realtimeSinceStartup + 4f;
            while (m.C.N < shared && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.2f);
            }
            yield return new WaitForSeconds(1f);
            int got = Counter().Of(a[0]);
            r.Info = $"{got} step(s) of {a[0]} acted on our player (want {own}), its shared step ran {m.C.N} time(s) (want {shared})";
            if (got != own || m.C.N != shared)
            {
                r.Fail(r.Info);
            }
        }

        private static string Name(Object o)
        {
            if (o == null)
            {
                return "null";
            }
            return o.name;
        }

        private static string Req(EventTriggerRequirement q)
        {
            string state = "";
            if (q.type == EventTriggerRequirement.Type.playerState)
            {
                state = "/" + q.playerState;
            }
            return $"{q.type}{state} item={Name(q.itemType)} key='{q.keyType}' n={q.amount} mod={q.activeModifier} dest={Name(q.destTransform)}";
        }

        private static string Ev(GameEvent e)
        {
            string head = $"{e.type} '{e.Value}' '{e.Value2}' mod={e.activeModifier}/{e.activeModifier2}/{e.relentlessPursuit} main={e.mainScriptModify} n={e.amount} delay={e.delay}";
            List<Object> prefabs = e.prefabList;
            if (prefabs == null)
            {
                prefabs = new List<Object>();
            }
            string prefabNames = string.Join(",", prefabs.Select(Name));
            List<GameObject> targets = e.targetGameObjects;
            if (targets == null)
            {
                targets = new List<GameObject>();
            }
            string targetNames = string.Join(",", targets.Select(g =>
            {
                if (g == null)
                {
                    return "null";
                }
                return $"{g.name}@{Fmt(g.transform.position)}";
            }));
            List<string> uniques = e.targetUniqueObjects;
            if (uniques == null)
            {
                uniques = new List<string>();
            }
            string uniqueNames = string.Join(",", uniques);
            List<Location.GetObject> places = e.locationObjects;
            int count = 0;
            if (places != null)
            {
                count = places.Count;
            }
            string note = "";
            if (e.disabled)
            {
                note = " disabled";
            }
            return head + $" prefabs=[{prefabNames}]" + $" targets=[{targetNames}|{uniqueNames}]" + $" loc={count}{note}";
        }

        /// <summary>
        /// "dive-scan PATTERN": log every trigger (with its requirements) and every GameEvents under the objects whose
        /// root's name contains PATTERN, and anything anywhere that names an oxygen tank or a dive.
        /// </summary>
        private static IEnumerator Scan(string[] a, Result r)
        {
            string pat = "cellar";
            if (a.Length > 0)
            {
                pat = a[0];
            }
            var sb = new System.Text.StringBuilder();
            int n = 0;
            bool Wanted(Component c)
            {
                if (c == null)
                {
                    return false;
                }
                var root = c.transform.root;
                return c.name.IndexOf(pat, System.StringComparison.OrdinalIgnoreCase) >= 0
                    || root.name.IndexOf(pat, System.StringComparison.OrdinalIgnoreCase) >= 0
                    || c.GetComponentsInParent<Transform>(true).Any(t => t.name.IndexOf(pat, System.StringComparison.OrdinalIgnoreCase) >= 0);
            }
            bool Mentions(GameEvents g) => g != null && g.events.Any(e =>
            {
                if (e == null)
                {
                    return false;
                }
                string content = e.Value;
                if (content == null)
                {
                    content = "";
                }
                if (content.IndexOf("dive", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
                List<Object> prefabs = e.prefabList;
                if (prefabs == null)
                {
                    prefabs = new List<Object>();
                }
                return prefabs.Any(o => o != null && o.name.Contains("oxygen"));
            });
            foreach (var et in Object.FindObjectsOfType<EventTriggers>(true))
            {
                if (et == null || et.eventTriggers == null)
                {
                    continue;
                }
                bool any = Wanted(et) || et.eventTriggers.Any(t => t != null && (Mentions(t.gameEvents) || Mentions(t.gameEventsElse)
                    || t.eventRequirements.Any(q => q != null && q.itemType != null && q.itemType.name.Contains("oxygen"))));
                if (!any)
                {
                    continue;
                }
                sb.Append($"\n{et.transform.root.name}/{et.name} at {Fmt(et.transform.position)} active={et.gameObject.activeInHierarchy}");
                foreach (var t in et.eventTriggers)
                {
                    if (t == null)
                    {
                        continue;
                    }
                    n++;
                    string text = "";
                    if (t.disabled)
                    {
                        text = " disabled";
                    }
                    sb.Append($"\n  > {t.type} '{t.reactsToItemType}' fired={t.fired} multi={t.multipleFire}{text} removeItem={t.removeItemThatTriggeredMe}");
                    foreach (var q in t.eventRequirements)
                    {
                        if (q != null)
                        {
                            sb.Append($"\n    req {Req(q)}");
                        }
                    }
                    foreach (var (label, ge) in new[] { ("do", t.gameEvents), ("else", t.gameEventsElse), ("exit", t.gameEventsExit) })
                    {
                        if (ge != null)
                        {
                            sb.Append($"\n    {label}: {ge.name} multi={ge.multipleFire}");
                            foreach (var e in ge.events)
                            {
                                if (e != null)
                                {
                                    sb.Append($"\n      {Ev(e)}");
                                }
                            }
                        }
                    }
                }
            }
            foreach (var g in Object.FindObjectsOfType<GameEvents>(true))
            {
                if (g == null || g.GetComponent<EventTriggers>() != null || !(Wanted(g) || Mentions(g)))
                {
                    continue;
                }
                sb.Append($"\n[events] {g.transform.root.name}/{g.name} at {Fmt(g.transform.position)} multi={g.multipleFire}");
                foreach (var e in g.events)
                {
                    if (e != null)
                    {
                        sb.Append($"\n      {Ev(e)}");
                    }
                }
            }
            foreach (var type in new[] { "oxygenTank_full", "oxygenTank_empty" })
            {
                var db = Singleton<ItemsDatabase>.Instance;
                GameObject prefab = null;
                if (db != null && db.itemsDict.TryGetValue(type, out var path))
                {
                    prefab = Resources.Load(path) as GameObject;
                }
                InvItem inv = null;
                if (prefab != null)
                {
                    inv = prefab.GetComponent<InvItem>();
                }
                var jd = Singleton<JournalDatabase>.Instance;
                sb.Append($"\n[item] {type}: prefab={(prefab != null)} important={inv?.isImportantItem} journalItem={(jd != null && jd.itemsDict.ContainsKey(type))}" +
                          $" inJournal={Singleton<UI>.Instance?.journal?.itemsDict?.ContainsKey(type)} carried={MapScenarioWorld.CarriedOf(type)}");
            }
            // Every loaded step that acts on the player (ScriptedEvents.TargetsPlayer), by type and function, and how
            // many of them also name other targets.
            var onPlayer = new Dictionary<string, int>();
            int mixed = 0;
            foreach (var g in Object.FindObjectsOfType<GameEvents>(true))
            {
                foreach (var e in g.events)
                {
                    if (e != null && ScriptedEvents.TargetsPlayer(e))
                    {
                        string value = "";
                        if (e.type == GameEvent.Type.runFunction)
                        {
                            value = " " + e.Value;
                        }
                        GameEvent.Type kind = e.type;
                        string step = "";
                        if (ScriptedEvents.IsPlayerStep(e.type))
                        {
                            step = " (player step)";
                        }
                        string k = kind + value + step;
                        int value2 = 0;
                        if (onPlayer.TryGetValue(k, out var c))
                        {
                            value2 = c;
                        }
                        onPlayer[k] = value2 + 1;
                        List<GameObject> targetGameObjects = e.targetGameObjects;
                        int count = 0;
                        if (targetGameObjects != null)
                        {
                            count = targetGameObjects.Count;
                        }
                        List<string> targetUniqueObjects = e.targetUniqueObjects;
                        int count2 = 0;
                        if (targetUniqueObjects != null)
                        {
                            count2 = targetUniqueObjects.Count;
                        }
                        if (count + count2 > 1)
                        {
                            mixed++;
                        }
                    }
                }
            }
            sb.Append($"\n[on player] {string.Join("; ", onPlayer.OrderByDescending(x => x.Value).Select(x => $"{x.Key} x{x.Value}"))}; with other targets too: {mixed}");
            UniqueObjects instance = Singleton<UniqueObjects>.Instance;
            GameObject uo = null;
            if (instance != null)
            {
                uo = instance.getObject("player");
            }
            sb.Append($"\n[unique] player -> {Name(uo)} is Player.Instance: {uo != null && Player.Instance != null && uo == Player.Instance.gameObject}");
            Plugin.Log.LogInfo($"[dive] scan {pat}:{sb}");
            r.Info = $"{n} triggers (in the log)";
            yield break;
        }
    }
}
