using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Steps of the two-game map test for story progression that needed the
    /// host (tools/tests/map_scenario_story2.py).
    ///
    /// World triggers a joiner caused (a refreshWhenTurnedOn of a machine they
    /// switched on, the onDeath of an NPC they killed) ran whole on the host,
    /// so their player part (an item given) went to the host's player. Now the
    /// player who caused it gets it (ScriptedEvents.SendCaused), and the host
    /// runs the host's and the shared part.
    /// </summary>
    public static class MapScenarioStory2
    {
        private const string Item = "rope";

        private sealed class Made
        {
            public EventTriggers Et;
            public global::EventTrigger T;
            public LaterTestCounter C;
            public int Had;
        }

        private static readonly Dictionary<string, Made> _made =
            new Dictionary<string, Made>();

        public static void Register()
        {
            MapScenario.Add("caused-setup", CausedSetup);
            MapScenario.Add("caused-check", CausedCheck, timeout: 20f);
            MapScenario.Add("owed-loaded", OwedLoaded);
            MapScenario.Add("owed-reload", OwedReload);
        }

        /// <summary>
        /// "owed-loaded ID" (after later-setup ID): the trigger and its events
        /// are marked fired, as a world loaded after someone else set it off
        /// has them (a mid-game join, a reload: no EventFired came for it).
        /// </summary>
        private static IEnumerator OwedLoaded(string[] a, Result r)
        {
            EventTriggers et = null;
            if (a.Length > 0)
            {
                GameObject gameObject = GameObject.Find("dwmp_later_" + a[0]);
                if (gameObject != null)
                {
                    et = gameObject.GetComponent<EventTriggers>();
                }
                else
                {
                    et = null;
                }
            }

            if (et == null || et.eventTriggers.Count == 0)
            {
                r.Fail("no such trigger (later-setup first)");
                yield break;
            }

            var t = et.eventTriggers[0];
            t.fired = true;
            if (t.gameEvents != null)
            {
                t.gameEvents.fired = true;
            }

            yield return null;
            r.Info =
                $"{et.name} marked fired as loaded; owed to our player "
                    + $"{ScriptedEvents.IsOwed(t)}";
        }

        /// <summary>
        /// "owed-reload" (host): the host's game saves and is started again:
        /// what's owed is written and read back.
        /// </summary>
        private static IEnumerator OwedReload(string[] a, Result r)
        {
            OwedParts.TestReload();
            yield return null;
            r.Info = "who is owed what written beside the save and read back";
        }

        private static Vector3 At(string dx, string dz) =>
            Centre + new Vector3(
                float.Parse(
                    dx, System.Globalization.CultureInfo.InvariantCulture),
                0f,
                float.Parse(
                    dz, System.Globalization.CultureInfo.InvariantCulture));

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
        /// Room for the item in the bags (a full inventory drops it on the
        /// ground instead, out of what we count).
        /// </summary>
        private static void MakeRoom()
        {
            var bag = Player.Instance.Inventory;
            if (bag.slots.Exists(
                s => s != null && InvItemClass.isNull(s.invItem)))
            {
                return;
            }

            var last = bag.slots.FindLast(
                s => s != null && !InvItemClass.isNull(s.invItem)
                    && s.invItem.type != Item);
            if (last != null)
            {
                bag.removeItemAmountFromPlayer(
                    last.invItem.type, last.invItem.amount);
            }
        }

        /// <summary>
        /// A GameEvents on go: a shared runFunction on a counter, and one Item
        /// given to the player.
        /// </summary>
        private static GameEvents Events(GameObject go, Made m, Result r)
        {
            var ge = go.AddComponent<GameEvents>();
            ge.multipleFire = false;
            m.C = Root(go.name
                    + "_count",
                        go.transform.position).AddComponent<LaterTestCounter>();
            ge.events.Add(new GameEvent
            {
                type = GameEvent.Type.runFunction,
                Value = "DwmpLaterCount",
                targetGameObjects = new List<GameObject> { m.C.gameObject },
                eventAmount = 1
            });

            var db = Singleton<ItemsDatabase>.Instance;
            GameObject prefab = null;
            if (db != null && db.itemsDict.TryGetValue(Item, out var path))
            {
                prefab = Resources.Load(path) as GameObject;
            }

            if (prefab == null || prefab.GetComponent<InvItem>() == null)
            {
                r.Fail($"no item prefab {Item}");
                return null;
            }

            ge.events.Add(new GameEvent
            {
                type = GameEvent.Type.addOrRemoveInvItem,
                activeModifier = true,
                prefabList = new List<Object> { prefab },
                amount = 1,
                eventAmount = 1
            });

            return ge;
        }

        /// <summary>
        /// "caused-setup ID LAMP DX DZ" (both games): a once-only
        /// refreshWhenTurnedOn trigger ID at the centre plus (DX, DZ), set off
        /// by the switchable item LAMP while it's on (an ActionWhenTurnedOn
        /// added to it, as the game's machines have). Its events: a shared
        /// counter and one Item given to the player.
        /// </summary>
        private static IEnumerator CausedSetup(string[] a, Result r)
        {
            if (a.Length < 4)
            {
                r.Fail("caused-setup ID LAMP DX DZ");
                yield break;
            }

            var lamp = MapScenarioWorld.Find<Item>(a[1], i => i.switchable);
            if (lamp == null)
            {
                r.Fail($"nothing switchable called {a[1]}");
                yield break;
            }

            if (lamp.isOn)
            {
                lamp.turnOff();
            }

            var m = new Made();
            var go = Root("dwmp_caused_" + a[0], At(a[2], a[3]));
            var ge = Events(go, m, r);
            if (ge == null)
            {
                yield break;
            }

            m.Et = go.AddComponent<EventTriggers>();
            m.T = new global::EventTrigger
            {
                type = global::EventTrigger.Type.refreshWhenTurnedOn,
                multipleFire = false,
                gameEvents = ge
            };

            m.Et.eventTriggers.Add(m.T);
            m.T.thisEventTriggers = m.Et;
            ActionWhenTurnedOn awto = lamp.GetComponent<ActionWhenTurnedOn>();
            if (awto == null)
            {
                awto = lamp.gameObject.AddComponent<ActionWhenTurnedOn>();
            }

            awto.actions.Add(new ActionWhenTurnedOn.Action
            {
                type = ActionWhenTurnedOn.Action.Type.refreshWhenTurnedOn,
                destTransform = go.transform,
                onUpdate = true,
                interval = 0.5f,
                requirements = new List<EventTriggerRequirement>(),
            });

            MakeRoom();
            m.Had = MapScenarioWorld.CarriedOf(Item);
            _made[a[0]] = m;
            yield return null;
            r.Info =
                $"trigger {a[0]} "
                    + $"{ObjRef.Of(go)} at {Fmt(go.transform.position)}, set off by {lamp.name} {ObjRef.Of(lamp.gameObject)} while on; carrying {m.Had} {Item}";
        }

        /// <summary>
        /// "caused-check ID N GOT": the shared part of ID ran N times here, and
        /// our player got GOT Item from it.
        /// </summary>
        private static IEnumerator CausedCheck(string[] a, Result r)
        {
            if (a.Length < 3 || !_made.TryGetValue(a[0], out var m)
                || m.C == null)
            {
                r.Fail("caused-check ID N GOT (caused-setup first)");
                yield break;
            }

            int want = int.Parse(a[1]), wantGot = int.Parse(a[2]);
            float until = Time.realtimeSinceStartup + 8f;
            while ((m.C.N < want
                    || MapScenarioWorld.CarriedOf(Item) - m.Had < wantGot)
                && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.2f);
            }

            yield return new WaitForSeconds(1.5f);
            int got = MapScenarioWorld.CarriedOf(Item) - m.Had;
            r.Info =
                $"the shared part of {a[0]} ran {m.C.N} time(s) here (want "
                    + $"{want}), our player got {got} {Item} (want {wantGot}); "
                    + $"fired {m.T.fired}";
            if (m.C.N != want || got != wantGot)
            {
                r.Fail(r.Info);
            }
        }
    }
}
