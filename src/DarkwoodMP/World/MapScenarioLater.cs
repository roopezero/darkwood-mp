using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>For MapScenarioLater: a runFunction step calls this (SendMessage); how many times a shared part ran here.</summary>
    public sealed class LaterTestCounter : MonoBehaviour
    {
        public int N;
        private void DwmpLaterCount() => N++;
    }

    /// <summary>
    /// Steps of the two-game map test for once-only triggers with player steps (tools/tests/map_scenario_later.py).
    ///
    /// A once-only trigger's player steps (a trip somewhere, an item given) ran only for whoever set it off; the next
    /// player's game had it marked fired and gave them nothing, so they were left on the wrong side of a hole or a
    /// ladder. Now each later player gets their own part of it (ScriptedEvents.LaterStep), once, and the shared part
    /// still runs once.
    ///
    /// And a trigger set off the moment a player arrives somewhere (after a trip) reached the host before the
    /// player's new position did, and was refused as too far away; the client now sends its position first.
    ///
    /// Each trigger is made the same in both games: a root object with its own name at a spot from the centre (an
    /// ObjRef by name and place), with a shared step (a runFunction on a counter object: how many times the shared
    /// part ran in this game), and for "later" an ordinary item given and a trip to a marker beside it.
    /// </summary>
    public static class MapScenarioLater
    {
        private const string Item = "rope";

        private sealed class Made
        {
            public EventTriggers Et;
            public global::EventTrigger T;
            public LaterTestCounter C;
            public Transform Marker;
        }
        private static readonly Dictionary<string, Made> _made = new Dictionary<string, Made>();

        public static void Register()
        {
            MapScenario.Add("later-setup", Setup);
            MapScenario.Add("later-fire", Fire, timeout: 30f);
            MapScenario.Add("later-again", Again, timeout: 20f);
            MapScenario.Add("later-count", Count, timeout: 15f);
            MapScenario.Add("arrive-go", Arrive, timeout: 40f);
        }

        private static Vector3 At(string dx, string dz) =>
            Centre + new Vector3(float.Parse(dx, System.Globalization.CultureInfo.InvariantCulture), 0f, float.Parse(dz, System.Globalization.CultureInfo.InvariantCulture));

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
        /// "later-setup ID DX DZ [trip DX2 DZ2] [item] [count]" (both games): a once-only area trigger ID at the centre
        /// plus (DX, DZ). Its events: "count" a shared runFunction on its counter; "item" one Item given to the player
        /// (addOrRemoveInvItem, activeModifier); "trip DX2 DZ2" the player taken to a marker at the centre plus
        /// (DX2, DZ2) (transportPlayerToObject, no chapter).
        /// </summary>
        private static IEnumerator Setup(string[] a, Result r)
        {
            if (a.Length < 3)
            {
                r.Fail("later-setup ID DX DZ [trip DX2 DZ2] [item] [count]");
                yield break;
            }
            string id = a[0];
            var pos = At(a[1], a[2]);
            var m = new Made();
            var go = Root("dwmp_later_" + id, pos);
            var ge = go.AddComponent<GameEvents>();
            ge.multipleFire = false;
            var parts = new List<string>();
            for (int i = 3; i < a.Length; i++)
            {
                if (a[i] == "count")
                {
                    m.C = Root("dwmp_later_count_" + id, pos).AddComponent<LaterTestCounter>();
                    ge.events.Add(new GameEvent { type = GameEvent.Type.runFunction, Value = "DwmpLaterCount", targetGameObjects = new List<GameObject> { m.C.gameObject }, eventAmount = 1 });
                    parts.Add("count");
                }
                else if (a[i] == "item")
                {
                    var db = Singleton<ItemsDatabase>.Instance;
                    GameObject prefab = null;
                    if (db != null && db.itemsDict.TryGetValue(Item, out var path))
                    {
                        prefab = Resources.Load(path) as GameObject;
                    }
                    InvItem inv = null;
                    if (prefab != null)
                    {
                        inv = prefab.GetComponent<InvItem>();
                    }
                    if (inv == null)
                    {
                        r.Fail($"no item prefab {Item}");
                        yield break;
                    }
                    if (inv.isImportantItem)
                    {
                        r.Fail($"{Item} is an important item: a later player wouldn't get it");
                        yield break;
                    }
                    ge.events.Add(new GameEvent { type = GameEvent.Type.addOrRemoveInvItem, activeModifier = true, prefabList = new List<Object> { prefab }, amount = 1, eventAmount = 1 });
                    parts.Add("item");
                }
                else if (a[i] == "trip" && i + 2 < a.Length)
                {
                    m.Marker = Root("dwmp_later_to_" + id, At(a[i + 1], a[i + 2])).transform;
                    ge.events.Add(new GameEvent { type = GameEvent.Type.transportPlayerToObject, targetGameObjects = new List<GameObject> { m.Marker.gameObject }, floatValue = 0.1f, magnitude = 0.1f, eventAmount = 1 });
                    parts.Add($"trip to {Fmt(m.Marker.position)}");
                    i += 2;
                }
            }
            m.Et = go.AddComponent<EventTriggers>();
            m.T = new global::EventTrigger { type = global::EventTrigger.Type.area, multipleFire = false, gameEvents = ge };
            m.Et.eventTriggers.Add(m.T);
            m.T.thisEventTriggers = m.Et; // EventTriggers.Awake ran before the trigger was in its list
            _made[id] = m;
            yield return null;
            r.Info = $"trigger {id} {ObjRef.Of(go)} at {Fmt(pos)}: {string.Join(", ", parts)}";
        }

        /// <summary>Our player walks into trigger ID (as EventTriggers.OnTriggerEnter does).</summary>
        private static void SetOff(Made m)
        {
            AsPlayer(() => m.Et.fireEventTrigger(global::EventTrigger.Type.area));
            m.Et.entered++;
        }

        /// <summary>
        /// "later-fire ID": our player walks to trigger ID and sets it off; its player steps must run for us (the item
        /// given, the trip taken), whether we're the first to set it off or a later player.
        /// </summary>
        private static IEnumerator Fire(string[] a, Result r)
        {
            if (a.Length < 1 || !_made.TryGetValue(a[0], out var m) || m.Et == null)
            {
                r.Fail("no such trigger (later-setup first)");
                yield break;
            }
            yield return Walk(m.Et.transform.position, 100f);
            yield return new WaitForSeconds(0.5f);
            // Room for the item in the bags (a full inventory drops it on the ground instead, out of what we count).
            var bag = Player.Instance.Inventory;
            string made = "";
            if (!bag.slots.Exists(s => s != null && InvItemClass.isNull(s.invItem)))
            {
                var last = bag.slots.FindLast(s => s != null && !InvItemClass.isNull(s.invItem) && s.invItem.type != Item);
                if (last != null)
                {
                    made = $" (made room: took {last.invItem.type}x{last.invItem.amount})";
                    bag.removeItemAmountFromPlayer(last.invItem.type, last.invItem.amount);
                }
            }
            int had = MapScenarioWorld.CarriedOf(Item);
            bool owed = ScriptedEvents.IsOwed(m.T);
            bool wasFired = m.T.fired;
            SetOff(m);
            bool wantItem = m.T.gameEvents.events.Exists(e => e.type == GameEvent.Type.addOrRemoveInvItem);
            float until = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < until && !(Arrived(m) && (!wantItem || MapScenarioWorld.CarriedOf(Item) > had)))
            {
                yield return new WaitForSeconds(0.1f);
            }
            yield return new WaitForSeconds(0.5f);
            int got = MapScenarioWorld.CarriedOf(Item) - had;
            r.Info = $"{(wasFired ? "already fired here" : "first")}, owed {owed}: got {got} {Item}, at the marker {Arrived(m)}, "
                + $"shared part ran {m.C?.N ?? -1} time(s) here, still owed {ScriptedEvents.IsOwed(m.T)}{made}; bags {MapScenarioWorld.Contents(bag)}";
            if (wantItem && got != 1)
            {
                r.Fail($"our player didn't get its {Item} (got {got}): {r.Info}");
            }
            else if (!Arrived(m))
            {
                r.Fail($"our player wasn't taken to the marker: {r.Info}");
            }
            Core.forbidInputs = false;
            MapScenario.Place(Centre); // back for the next steps (a jump MoveCheck takes as a teleport)
            yield return new WaitForSeconds(1f);
        }

        private static bool Arrived(Made m) =>
            m.Marker == null || StateDump.Flat(Player.Instance.transform.position - m.Marker.position) < 150f;

        /// <summary>"later-again ID": our player sets trigger ID off again: nothing of it may run a second time for us.</summary>
        private static IEnumerator Again(string[] a, Result r)
        {
            if (a.Length < 1 || !_made.TryGetValue(a[0], out var m) || m.Et == null)
            {
                r.Fail("no such trigger (later-setup first)");
                yield break;
            }
            yield return Walk(m.Et.transform.position, 100f);
            yield return new WaitForSeconds(0.5f);
            int had = MapScenarioWorld.CarriedOf(Item);
            int ran = m.C?.N ?? 0;
            SetOff(m);
            yield return new WaitForSeconds(3f);
            int got = MapScenarioWorld.CarriedOf(Item) - had;
            r.Info = $"got {got} {Item}, at the marker {m.Marker != null && Arrived(m)}, shared part ran {(m.C?.N ?? 0) - ran} more time(s)";
            if (got != 0 || (m.Marker != null && Arrived(m)) || (m.C?.N ?? 0) != ran)
            {
                r.Fail($"it ran again for us: {r.Info}");
            }
            Core.forbidInputs = false;
            MapScenario.Place(Centre);
        }

        /// <summary>"later-count ID N": the shared part of trigger ID ran N times in this game.</summary>
        private static IEnumerator Count(string[] a, Result r)
        {
            if (a.Length < 2 || !_made.TryGetValue(a[0], out var m) || m.C == null)
            {
                r.Fail("no such trigger with a counter (later-setup first)");
                yield break;
            }
            int want = int.Parse(a[1]);
            float until = Time.realtimeSinceStartup + 5f;
            while (m.C.N < want && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.2f);
            }
            yield return new WaitForSeconds(1f);
            r.Info = $"the shared part of {a[0]} ran {m.C.N} time(s) here (want {want}); fired {m.T.fired}, owed to our player {ScriptedEvents.IsOwed(m.T)}";
            if (m.C.N != want)
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>
        /// "arrive-go TRIP AT": our player sets off TRIP (a trip to a marker far away) and, the frame it arrives, walks
        /// into AT (a trigger at that marker). The host must take AT (its shared part runs here once it said yes),
        /// though its stand-in for us was still where we left from.
        /// </summary>
        private static IEnumerator Arrive(string[] a, Result r)
        {
            if (a.Length < 2 || !_made.TryGetValue(a[0], out var trip) || !_made.TryGetValue(a[1], out var at) || trip.Marker == null || at.C == null)
            {
                r.Fail("arrive-go TRIP AT (later-setup both first: TRIP with a trip, AT with a counter)");
                yield break;
            }
            yield return Walk(trip.Et.transform.position, 100f);
            yield return new WaitForSeconds(0.5f);
            int refusedBefore = Authority.RolledBack;
            var p = Player.Instance;
            SetOff(trip);
            float until = Time.realtimeSinceStartup + 10f;
            while (!Arrived(trip) && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
            if (!Arrived(trip))
            {
                r.Fail($"the trip didn't take us to {Fmt(trip.Marker.position)} (at {Fmt(p.transform.position)})");
                yield break;
            }
            float from = StateDump.Flat(trip.Et.transform.position - at.Et.transform.position);
            SetOff(at); // the same frame we arrived
            until = Time.realtimeSinceStartup + 6f;
            while (at.C.N < 1 && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.1f);
            }
            r.Info = $"arrived {from:0} from where we left, set off {a[1]} at once: its shared part ran {at.C.N} time(s) here, requests refused {Authority.RolledBack - refusedBefore}";
            if (at.C.N != 1)
            {
                r.Fail($"the host didn't take the trigger we arrived at: {r.Info}");
            }
            Core.forbidInputs = false;
            yield return new WaitForSeconds(2.5f); // the trip's own fade-in ends
            Core.forbidInputs = false;
            MapScenario.Place(Centre);
            yield return new WaitForSeconds(1f);
        }
    }
}
