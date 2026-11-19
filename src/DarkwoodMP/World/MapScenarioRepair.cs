using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps for repairs (tools/tests/map_scenario_repair.py): a broken door rebuilt with the hammer and a
    /// broken piece of furniture rebuilt from its construction menu, by the client, must stay repaired in both games.
    ///
    ///   rp-door N            the Nth door from the centre (MapScenarioWorld.Nth), noted for the other steps.
    ///   rp-door-break        our player breaks it (Door.destroyDoor).
    ///   rp-door-fix          our player rebuilds it: the hammer's three strokes (Door.barricade(byPlayer), which on
    ///                        a broken door calls unDestroy).
    ///   rp-furniture DX DZ   the breakable item nearest centre + (DX, DZ), noted; if it has no construction menu for
    ///                        when it's broken, it's given one (in every game that runs the step) with one option.
    ///   rp-furniture-break   our player smashes it (Item.getHit).
    ///   rp-furniture-fix     our player builds it again (Constructible.construct, as the progress bar's end does).
    ///   rp-hold SECS         for SECS, neither the door nor the furniture may break here; both repaired at the end.
    /// </summary>
    public static class MapScenarioRepair
    {
        private static Player P => Player.Instance;
        private static Door _door;
        private static Item _item;

        public static void Register()
        {
            Add("rp-door", RpDoor);
            Add("rp-door-break", RpDoorBreak);
            Add("rp-door-fix", RpDoorFix);
            Add("rp-furniture", RpFurniture);
            Add("rp-furniture-break", RpFurnitureBreak);
            Add("rp-furniture-fix", RpFurnitureFix);
            Add("rp-hold", RpHold, timeout: 60f);
        }

        private static string DoorInfo => _door == null ? "door gone" : $"{_door.name} {MapScenarioWorld.Ref(_door)} broken={_door.destroyed} health {_door.health}";
        private static string ItemInfo => _item == null ? "item gone" : $"{_item.name} {MapScenarioWorld.Ref(_item)} broken={_item.destroyed} health {_item.health}/{_item.maxHealth}";

        private static IEnumerator RpDoor(string[] a, Result r)
        {
            _door = a.Length > 0 ? MapScenarioWorld.Nth<Door>(a[0]) : null;
            if (_door == null)
            {
                r.Fail("usage: rp-door N (no such door)");
                yield break;
            }
            r.Info = $"{DoorInfo} at {Fmt(_door.transform.position)}";
        }

        private static IEnumerator RpDoorBreak(string[] a, Result r)
        {
            if (_door == null)
            {
                r.Fail("no door (rp-door first)");
                yield break;
            }
            yield return Walk(_door.transform.position);
            if (!_door.destroyed)
            {
                AsPlayer(() => _door.destroyDoor(false));
            }
            yield return new WaitForSeconds(1f);
            r.Info = DoorInfo;
            if (_door == null || !_door.destroyed)
            {
                r.Fail($"{r.Info}: not broken");
            }
        }

        private static IEnumerator RpDoorFix(string[] a, Result r)
        {
            if (_door == null)
            {
                r.Fail("no door (rp-door first)");
                yield break;
            }
            yield return Walk(_door.transform.position);
            if (!_door.destroyed)
            {
                r.Fail($"{DoorInfo}: it isn't broken here");
                yield break;
            }
            for (int i = 0; i < 3 && _door != null && _door.destroyed; i++)
            {
                AsPlayer(() => _door.barricade(byPlayer: true));
                yield return new WaitForSeconds(0.3f);
            }
            yield return new WaitForSeconds(1.5f); // the host's answer
            r.Info = DoorInfo;
            if (_door == null || _door.destroyed)
            {
                r.Fail($"{r.Info}: not repaired (or broken again)");
            }
        }

        private static IEnumerator RpFurniture(string[] a, Result r)
        {
            var at = Centre + new Vector3(F(a[0]), 0f, F(a[1]));
            _item = Nearest(Object.FindObjectsOfType<Item>().Where(i => i.destructible && !i.hasInventory && i.GetComponent<Explodes>() == null
                && i.GetComponent<Trigger>() == null && i.GetComponent<ProxyItem>() == null && ObjRef.Of(i.gameObject).Valid
                && StateDump.Flat(i.transform.position - at) < 200f), at);
            if (_item == null)
            {
                r.Fail("no breakable furniture there");
                yield break;
            }
            var c = _item.GetComponent<Constructible>();
            bool made = c == null;
            if (made)
            {
                c = _item.gameObject.AddComponent<Constructible>();
                c.onlyWhenDestroyed = true;
                c.dontDestroyMeWhenConstructed = true;
                c.options = new List<Constructible.Option> { new Constructible.Option { name = "repair", constructTime = 1f, requirements = new List<ConstructionRequirement>() } };
            }
            r.Info = $"{ItemInfo} at {Fmt(_item.transform.position)}, construction menu {(made ? "added" : "its own")} (only when broken: {c.onlyWhenDestroyed}, {c.options.Count} options)";
        }

        private static IEnumerator RpFurnitureBreak(string[] a, Result r)
        {
            if (_item == null)
            {
                r.Fail("no furniture (rp-furniture first)");
                yield break;
            }
            yield return Walk(_item.transform.position);
            if (!_item.destroyed)
            {
                AsPlayer(() => _item.getHit(_item.health + 1, P.transform));
            }
            yield return new WaitForSeconds(1f);
            r.Info = ItemInfo;
            if (_item == null || !_item.destroyed)
            {
                r.Fail($"{r.Info}: not broken");
            }
        }

        private static IEnumerator RpFurnitureFix(string[] a, Result r)
        {
            if (_item == null)
            {
                r.Fail("no furniture (rp-furniture first)");
                yield break;
            }
            var c = _item.GetComponent<Constructible>();
            if (c == null)
            {
                r.Fail($"{ItemInfo}: no construction menu");
                yield break;
            }
            yield return Walk(_item.transform.position);
            if (!_item.destroyed)
            {
                r.Fail($"{ItemInfo}: it isn't broken here");
                yield break;
            }
            AsPlayer(() => c.construct(manual: false, 0));
            float until = Time.realtimeSinceStartup + 4f;
            while (_item != null && _item.destroyed && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.1f);
            }
            yield return new WaitForSeconds(1f);
            r.Info = ItemInfo;
            if (_item == null || _item.destroyed)
            {
                r.Fail($"{r.Info}: not repaired (or broken again)");
            }
        }

        private static IEnumerator RpHold(string[] a, Result r)
        {
            float secs = a.Length > 0 ? F(a[0]) : 5f;
            float until = Time.realtimeSinceStartup + secs;
            string broke = null;
            while (Time.realtimeSinceStartup < until && broke == null)
            {
                if (_door != null && _door.destroyed)
                {
                    broke = "the door broke again: " + DoorInfo;
                }
                else if (_item != null && _item.destroyed)
                {
                    broke = "the furniture broke again: " + ItemInfo;
                }
                yield return new WaitForSeconds(0.1f);
            }
            r.Info = $"{DoorInfo}; {ItemInfo}";
            if (broke != null)
            {
                r.Fail(broke);
            }
            else if (_door == null || _item == null)
            {
                r.Fail($"{r.Info}: not both here");
            }
        }
    }
}
