using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Steps of the two-game map test for story triggers that a player's shared action sets off
    /// (tools/tests/map_scenario_story.py).
    ///
    /// A door opened or closed, a thing switched on or off and a trap disarmed are shared actions: the other games
    /// apply them as replays. A trigger on the thing (onOpenDoor, onTurnOn, onDisarmed...) used to start in every
    /// replay as a whole event, so the host's player got the steps of the joiner who opened the door, and the joiner's
    /// own request came after it and was refused as already happened. Now only the player who did it sets the trigger
    /// off; the others run its shared part when it reaches them.
    /// </summary>
    public static class MapScenarioStory
    {
        public static void Register()
        {
            Add("act-setup", Setup);
            Add("act-do", Do, timeout: 40f);
            Add("act-count", Count, timeout: 15f);
        }

        private sealed class Made
        {
            public GameObject Thing;
            public LaterTestCounter C;
        }
        private static readonly Dictionary<string, Made> _made = new Dictionary<string, Made>();

        private static readonly AccessTools.FieldRef<Player, Item> TrapBeingDisarmed = AccessTools.FieldRefAccess<Player, Item>("trapBeingDisarmed");

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

        /// <summary>
        /// "door:N" (the Nth door from the centre, MapScenarioWorld.Nth), "trap:NAME" (an armed trap), "enemy" (the
        /// host's enemy nearest to our player) or an item's NAME.
        /// </summary>
        private static GameObject Thing(string spec)
        {
            if (spec == "enemy")
            {
                Character hostEnemyNear = MapScenarioWorld.HostEnemyNear(1500f);
                if (hostEnemyNear != null)
                {
                    return hostEnemyNear.gameObject;
                }
                return null;
            }
            if (spec.StartsWith("door:"))
            {
                // The Nth door that isn't broken in the save (the test world has broken ones, which the host won't open).
                int want = int.Parse(spec.Substring(5));
                for (int i = 0, whole = 0; i < 64; i++)
                {
                    var d = MapScenarioWorld.Nth<Door>(i.ToString());
                    if (d == null)
                    {
                        return null;
                    }
                    if (!d.destroyed && whole++ == want)
                    {
                        return d.gameObject;
                    }
                }
                return null;
            }
            if (spec.StartsWith("trap:"))
            {
                Trigger trigger = MapScenarioWorld.Find<Trigger>(spec.Substring(5), x => x.active && !x.triggered && x.canDisarm && x.GetComponent<Item>() != null);
                if (trigger != null)
                {
                    return trigger.gameObject;
                }
                return null;
            }
            Item item = MapScenarioWorld.Find<Item>(spec, i => i.switchable);
            if (item != null)
            {
                return item.gameObject;
            }
            return null;
        }

        /// <summary>
        /// "act-setup ID THING TYPE once|multi" (both games): a trigger of TYPE (onOpenDoor, onTurnOn, onDisarmed...)
        /// on THING with three runFunction steps: two on our player (the unique object "player" and the player's own
        /// object, counted by ID) and a shared one on a counter beside it. A trap is made to stay after a disarm and
        /// to be disarmed every time, so the trigger's object is still there when its event runs.
        /// </summary>
        private static IEnumerator Setup(string[] a, Result r)
        {
            if (a.Length < 4)
            {
                r.Fail("act-setup ID THING TYPE once|multi");
                yield break;
            }
            string id = a[0];
            var go = Thing(a[1]);
            if (go == null)
            {
                r.Fail($"no {a[1]} here");
                yield break;
            }
            var type = (global::EventTrigger.Type)System.Enum.Parse(typeof(global::EventTrigger.Type), a[2]);
            bool multi = a[3] == "multi";
            // So nothing in the test area breaks it during the steps (the host doesn't open a broken door).
            var door = go.GetComponent<Door>();
            if (door != null)
            {
                door.health = 100000;
            }
            var trap = go.GetComponent<Trigger>();
            if (trap != null)
            {
                trap.staysAfterDisarming = true;
                go.GetComponent<Item>().disarmChance = 2f;
            }
            var m = new Made { Thing = go };
            var old = GameObject.Find("dwmp_act_" + id);
            if (old != null)
            {
                Object.Destroy(old);
            }
            var root = new GameObject("dwmp_act_" + id);
            root.transform.position = go.transform.position;
            var ge = root.AddComponent<GameEvents>();
            ge.multipleFire = multi;
            Counter().N.Remove(id);
            m.C = root.AddComponent<LaterTestCounter>();
            ge.events.Add(new GameEvent { type = GameEvent.Type.runFunction, Value = "DwmpOwnCount", activeModifier2 = true, stringValue = id, targetUniqueObjects = new List<string> { "player" }, eventAmount = 1 });
            ge.events.Add(new GameEvent { type = GameEvent.Type.runFunction, Value = "DwmpOwnCount", activeModifier2 = true, stringValue = id, targetGameObjects = new List<GameObject> { Player.Instance.gameObject }, eventAmount = 1 });
            ge.events.Add(new GameEvent { type = GameEvent.Type.runFunction, Value = "DwmpLaterCount", targetGameObjects = new List<GameObject> { root }, eventAmount = 1 });
            EventTriggers et = go.GetComponent<EventTriggers>();
            if (et == null)
            {
                et = go.AddComponent<EventTriggers>();
            }
            var t = new global::EventTrigger { type = type, multipleFire = multi, gameEvents = ge, thisEventTriggers = et };
            et.eventTriggers.Add(t);
            _made[id] = m;
            yield return null;
            r.Info = $"trigger {id} #{et.eventTriggers.Count - 1} ({type}, {a[3]}) on {go.name} {ObjRef.Of(go)} at {Fmt(go.transform.position)}";
        }

        /// <summary>
        /// "act-do ID open|close|on|off|disarm|hit OWN": our player does it to ID's thing, as the player would; OWN steps
        /// must then act on our player (2, or 0).
        /// </summary>
        private static IEnumerator Do(string[] a, Result r)
        {
            if (a.Length < 3 || !_made.TryGetValue(a[0], out var m) || m.Thing == null)
            {
                r.Fail("act-do ID open|close|on|off|disarm OWN (act-setup first)");
                yield break;
            }
            int want = int.Parse(a[2]);
            var p = Player.Instance;
            yield return Walk(m.Thing.transform.position, 90f);
            yield return new WaitForSeconds(0.5f);
            var c = Counter();
            int had = c.Of(a[0]);
            string state;
            switch (a[1])
            {
                case "open":
                case "close":
                    {
                        var door = m.Thing.GetComponent<Door>();
                        bool open = a[1] == "open";
                        if (door.opened != open)
                        {
                            AsPlayer(() =>
                            {
                                if (open)
                                {
                                    door.open(p.transform.position, p.transform, 0f);
                                }
                                else
                                {
                                    door.close(p.transform);
                                }
                            });
                        }
                        yield return new WaitForSeconds(0.5f);
                        state = $"open={door.opened} broken={door.destroyed} health={door.health}";
                        if (door.opened != open)
                        {
                            r.Fail($"{m.Thing.name}: {state}");
                            yield break;
                        }
                        break;
                    }
                case "on":
                case "off":
                    {
                        var it = m.Thing.GetComponent<Item>();
                        bool on = a[1] == "on";
                        if (it.isOn != on)
                        {
                            AsPlayer(() =>
                            {
                                if (on)
                                {
                                    it.turnOn();
                                }
                                else
                                {
                                    it.turnOff();
                                }
                            });
                        }
                        yield return new WaitForSeconds(0.5f);
                        state = $"on={it.isOn}";
                        if (it.isOn != on)
                        {
                            r.Fail($"{m.Thing.name}: {state}");
                            yield break;
                        }
                        break;
                    }
                case "hit":
                    {
                        // A light hit by our player (a client's goes to the host, which applies it).
                        var ch = m.Thing.GetComponent<Character>();
                        AsPlayer(() => ch.getHit(1f, p.transform, false, true, false, true, false, false, false));
                        state = $"alive={ch.alive}";
                        break;
                    }
                default:
                    {
                        var it = m.Thing.GetComponent<Item>();
                        var trap = m.Thing.GetComponent<Trigger>();
                        AsPlayer(() =>
                        {
                            p.disarmingTrap = true;
                            TrapBeingDisarmed(p) = it;
                            p.progressBarCompleted();
                        });
                        float until0 = Time.realtimeSinceStartup + 4f;
                        while (trap != null && !trap.triggered && Time.realtimeSinceStartup < until0)
                        {
                            yield return null;
                        }
                        p.hidingInventory = false;
                        state = $"disarmed={trap != null && trap.triggered}";
                        break;
                    }
            }
            float until = Time.realtimeSinceStartup + 6f;
            while (Time.realtimeSinceStartup < until && c.Of(a[0]) - had < want)
            {
                yield return new WaitForSeconds(0.1f);
            }
            yield return new WaitForSeconds(1.5f);
            int got = c.Of(a[0]) - had;
            r.Info = $"{m.Thing.name} {state}: {got} step(s) acted on our player (want {want}), the shared step ran {m.C.N} time(s) here";
            if (got != want)
            {
                r.Fail(r.Info);
            }
            yield return Walk(Centre, 60f);
        }

        /// <summary>"act-count ID OWN SHARED": in this game, OWN steps of ID acted on our player and its shared step ran SHARED times.</summary>
        private static IEnumerator Count(string[] a, Result r)
        {
            if (a.Length < 3 || !_made.TryGetValue(a[0], out var m) || m.C == null)
            {
                r.Fail("act-count ID OWN SHARED (act-setup first)");
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
            string value = "";
            if (m.Thing != null && m.Thing.GetComponent<Door>() is Door d)
            {
                value = $"; door open={d.opened} broken={d.destroyed} health={d.health}";
            }
            r.Info = $"{got} step(s) of {a[0]} acted on our player (want {own}), its shared step ran {m.C.N} time(s) (want {shared})" + value;
            if (got != own || m.C.N != shared)
            {
                r.Fail(r.Info);
            }
        }
    }
}
