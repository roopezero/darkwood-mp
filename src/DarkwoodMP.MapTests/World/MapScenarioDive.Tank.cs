using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>The full oxygen tank, shared once filled.</summary>
    public static partial class MapScenarioDive
    {
        // ---- the full oxygen tank, once filled, for everyone (SharedTank) ---
        // For every player.

        private static readonly
            Dictionary<string, (EventTriggers et, Vector3 pos)> _tanks =
                new Dictionary<string, (EventTriggers, Vector3)>();

        private static GameObject TankPrefab()
        {
            var db = Singleton<ItemsDatabase>.Instance;
            if (db != null
                && db.itemsDict.TryGetValue(SharedTank.Item, out var path))
            {
                return Resources.Load(path) as GameObject;
            }

            return null;
        }

        /// <summary>
        /// "tank-setup ID DX DZ" (both games): an onActivate thing ID at the
        /// centre plus (DX, DZ) made as the cellar's passages are: a trigger
        /// that needs the full tank (playerState/haveItem oxygenTank_full) and
        /// counts "ID" on our player, and one that needs not having it and
        /// counts "ID-no".
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
            foreach (var (has, counted) in new[]
            {
                (true, id),
                (false, id + "-no")
            })
            {
                var g = new GameObject("dwmp_tank_events_" + counted);
                g.transform.SetParent(go.transform, false);
                var ge = g.AddComponent<GameEvents>();
                ge.multipleFire = true;
                ge.events.Add(new GameEvent
                {
                    type = GameEvent.Type.runFunction,
                    Value = "DwmpOwnCount",
                    activeModifier2 = true,
                    stringValue = counted,
                    targetUniqueObjects = new List<string> { "player" },
                    eventAmount = 1
                });

                var t = new global::EventTrigger
                {
                    type = global::EventTrigger.Type.onActivate,
                    multipleFire = true,
                    gameEvents = ge
                };

                t.eventRequirements.Add(new EventTriggerRequirement
                {
                    type = EventTriggerRequirement.Type.playerState,
                    playerState = Player.State.haveItem,
                    itemType = prefab,
                    activeModifier = has
                });

                et.eventTriggers.Add(t);
                t.thisEventTriggers = et;
            }

            Counter().N.Remove(id);
            Counter().N.Remove(id + "-no");
            _tanks[id] = (et, pos);
            yield return null;
            var inv = prefab.GetComponent<InvItem>();
            r.Info =
                $"{id} "
                    + $"{ObjRef.Of(go)} at {Fmt(pos)}; {SharedTank.Item}: "
                    + $"important item {inv.isImportantItem}, "
                    + $"stackable {inv.stackable}; filled {SharedTank.Filled}";
        }

        /// <summary>
        /// "tank-give": our player gets a full tank, and our game must set the
        /// flag.
        /// </summary>
        private static IEnumerator TankGive(string[] a, Result r)
        {
            // Room in the bags first (a full inventory drops it on the ground
            // instead).
            var bag = Player.Instance.Inventory;
            if (!bag.slots.Exists(
                s => s != null && InvItemClass.isNull(s.invItem)))
            {
                var last = bag.slots.FindLast(
                    s => s != null && !InvItemClass.isNull(s.invItem));
                if (last != null)
                {
                    bag.removeItemAmountFromPlayer(
                        last.invItem.type, last.invItem.amount);
                }
            }

            bag.addItemTypeToPlayer(SharedTank.Item, 1, dropIfNoRoom: true);
            float until = Time.realtimeSinceStartup + 8f;
            while (!SharedTank.Filled && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.2f);
            }

            r.Info =
                $"carried {MapScenarioWorld.CarriedOf(SharedTank.Item)}, "
                    + $"filled {SharedTank.Filled}";
            if (!SharedTank.Filled)
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>
        /// "tank-take": our player's full tanks are taken away (the flag
        /// stays).
        /// </summary>
        private static IEnumerator TankTake(string[] a, Result r)
        {
            int n = MapScenarioWorld.CarriedOf(SharedTank.Item);
            Player.Instance.Inventory.removeItemAmountFromPlayer(
                SharedTank.Item, n, includeAdditionalInventory: true);
            yield return null;
            r.Info =
                $"took {n}, carried "
                    + $"{MapScenarioWorld.CarriedOf(SharedTank.Item)}, filled "
                    + $"{SharedTank.Filled}";
            if (MapScenarioWorld.CarriedOf(SharedTank.Item) != 0)
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>
        /// "tank-flag 1|0": the flag is set here (it came from the other game),
        /// and is in what a save writes.
        /// </summary>
        private static IEnumerator TankFlag(string[] a, Result r)
        {
            bool want = a.Length > 0 && a[0] == "1";
            float until = Time.realtimeSinceStartup + 8f;
            while (SharedTank.Filled != want
                && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.2f);
            }

            var saved =
                new Flags.SaveState(Singleton<Flags>.Instance).flags.Exists(
                    x => x != null && x.name == SharedTank.Flag && x.isTrue);
            r.Info =
                $"filled {SharedTank.Filled} (want {want}), in the save "
                    + $"{saved}";
            if (SharedTank.Filled != want || saved != want)
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>
        /// "tank-fire ID YES NO": our player uses ID; its tank trigger must
        /// have run YES times for us in all, the no-tank one NO.
        /// </summary>
        private static IEnumerator TankFire(string[] a, Result r)
        {
            if (a.Length < 3 || !_tanks.TryGetValue(a[0], out var m)
                || m.et == null)
            {
                r.Fail("tank-fire ID YES NO (tank-setup first)");
                yield break;
            }

            int yes = int.Parse(a[1]), no = int.Parse(a[2]);
            yield return Walk(m.pos, 100f);
            yield return new WaitForSeconds(0.5f);
            AsPlayer(
                () => m.et.fireEventTrigger(
                    global::EventTrigger.Type.onActivate));
            var c = Counter();
            float until = Time.realtimeSinceStartup + 6f;
            while (Time.realtimeSinceStartup < until
                && c.Of(a[0]) + c.Of(a[0] + "-no") < yes + no)
            {
                yield return new WaitForSeconds(0.1f);
            }

            yield return new WaitForSeconds(1f);
            r.Info =
                $"with the tank ran {c.Of(a[0])} (want {yes}), without it "
                    + $"{c.Of(a[0] + "-no")} (want {no}); carried "
                    + $"{MapScenarioWorld.CarriedOf(SharedTank.Item)}, filled "
                    + $"{SharedTank.Filled}";
            if (c.Of(a[0]) != yes || c.Of(a[0] + "-no") != no)
            {
                r.Fail(r.Info);
            }
        }
    }
}
