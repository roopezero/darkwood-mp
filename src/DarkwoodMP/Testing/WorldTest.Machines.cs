using DarkwoodMP.World.Inventories;
using DarkwoodMP.World.Rolls;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.World.Scripted;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The world test's steps with machines, locks and things that break
    /// (WorldTest).
    /// </summary>
    public static partial class WorldTest
    {
        /// <summary>
        /// A generator, the workbench's storage, locks, a padlock, a crate,
        /// furniture smashed and an explosive set off.
        /// </summary>
        private static IEnumerator MachinesStep(
            Player player, Vector3 at, InvItemClass item)
        {
            // A generator: pour fuel into it as the player does, a little at a
            // time, and switch it on.
            var gen = Nearest(
                Object.FindObjectsOfType<Generator>().Where(g => !g.isSaw), at);
            if (gen != null)
            {
                Plugin.Log.LogInfo(
                    $"[world test] generator {gen.name} at "
                        + $"{gen.transform.position}: fuel {gen.fuel:0.0} of "
                        + $"{gen.maxFuel}, on={gen.isOn}");
                var genItem = gen.GetComponent<Item>();
                if (gen.isOn)
                {
                    if (genItem != null)
                    {
                        genItem.turnOff();
                    }

                    yield return new WaitForSeconds(1f);
                }

                for (int i = 0; i < 20; i++)
                {
                    gen.addFuel(1f);
                    yield return new WaitForSeconds(0.05f);
                }

                if (genItem != null)
                {
                    genItem.turnOn();
                }
                else
                {
                    gen.turnOn();
                }

                Plugin.Log.LogInfo(
                    "[world test] generator after pouring 20 and switching "
                        + $"on: fuel {gen.fuel:0.0}, on={gen.isOn}");
                yield return new WaitForSeconds(2f);
            }
            else
            {
                Plugin.Log.LogInfo("[world test] no generator found");
            }

            // The workbench's storage, slot by slot: put something in and take
            // it out again, as if the window were open; the observer then
            // changes it too (one change that stands, one that comes too late).
            var wb = Nearest(Object.FindObjectsOfType<Workbench>(), at);
            if (wb != null && wb.normalInventory != null)
            {
                var storage = wb.normalInventory;
                // Stand at it, as a player using it does (the observer follows
                // us; the host refuses changes from afar).
                var by = wb.transform.position
                    + (player.transform.position
                        - wb.transform.position).normalized * 120f;
                player.transform.position =
                    new Vector3(by.x, player.transform.position.y, by.z);
                yield return new WaitForSeconds(2f);
                var free = storage.slots.FindIndex(
                    sl => InvItemClass.isNull(sl.invItem));
                Plugin.Log.LogInfo(
                    $"[world test] workbench storage {storage.name}: "
                        + $"{storage.slots.Count} slots, first free {free}, "
                        + "workbench level "
                        + $"{Singleton<Controller>.Instance.workbenchLevel}");
                if (free >= 0)
                {
                    player.openedItemInventory2 = storage;
                    yield return new WaitForSeconds(0.6f);
                    storage.slots[free].inventory = storage;
                    storage.slots[free].createItem(item.type, 2);
                    yield return new WaitForSeconds(0.8f);
                    storage.slots[free].removeItem();
                    yield return new WaitForSeconds(0.8f);
                    player.openedItemInventory2 = null;
                    yield return new WaitForSeconds(6f); // the observer's turn
                    var got = storage.slots[free].invItem;
                    string contents;
                    if (InvItemClass.isNull(got))
                    {
                        contents = "empty";
                    }
                    else
                    {
                        contents = got.type + " x" + got.amount;
                    }

                    Plugin.Log.LogInfo(
                        $"[world test] storage slot {free} now: {contents}; "
                            + "workbench level "
                            + Singleton<Controller>.Instance.workbenchLevel);

                    // Sort & Stack (the observer pressed it too, as a client,
                    // during its turn): two partial stacks of one item in two
                    // free slots apart, then the button's action. They must end
                    // as one stack.
                    string stackType = DbItems()
                        .Where(
                            x => x.it.stackable && !x.it.hasAmmo
                                && x.it.maxAmount >= 5).Select(x => x.key)
                        .FirstOrDefault();
                    var frees = new List<int>();
                    for (int i = 0; i < storage.slots.Count; i++)
                    {
                        if (InvItemClass.isNull(storage.slots[i].invItem))
                        {
                            frees.Add(i);
                        }
                    }

                    if (stackType != null && frees.Count >= 2)
                    {
                        player.openedItemInventory2 = storage;
                        yield return new WaitForSeconds(0.6f);
                        int s1 = frees[0], s2 = frees[frees.Count - 1];
                        storage.slots[s1].inventory = storage;
                        storage.slots[s1].createItem(stackType, 2);
                        storage.slots[s2].inventory = storage;
                        storage.slots[s2].createItem(stackType, 1);
                        yield return new WaitForSeconds(0.8f);
                        int Filled() => storage.slots.Count(
                            sl => !InvItemClass.isNull(sl.invItem));
                        int stacksBefore = storage.slots.Count(
                            sl => !InvItemClass.isNull(sl.invItem)
                                && sl.invItem.type == stackType),
                            filledBefore = Filled();
                        CoopSession sorting = Coop.Current;
                        if (sorting != null)
                        {
                            sorting.Slots.SortPressed(storage);
                        }

                        yield return new WaitForSeconds(0.8f);
                        var mine = storage.slots.Where(
                            sl => !InvItemClass.isNull(sl.invItem)
                                && sl.invItem.type == stackType).ToList();
                        int firstEmpty = storage.slots.FindIndex(
                            sl => InvItemClass.isNull(sl.invItem));
                        bool packed = firstEmpty < 0
                            || storage.slots.Skip(firstEmpty).All(
                                sl => InvItemClass.isNull(sl.invItem));
                        string amounts = string.Join(
                            ", ", mine.Select(sl => "x" + sl.invItem.amount));
                        Plugin.Log.LogInfo(
                            $"[world test] sort & stack: {stackType} in "
                                + $"{stacksBefore} stack(s) before, "
                                + $"{mine.Count} after "
                                + $"({amounts}); filled slots "
                                + $"{filledBefore} -> {Filled()}; "
                                + $"items packed from slot 0: {packed}");
                        player.openedItemInventory2 = null;
                        yield return new WaitForSeconds(1f);
                    }
                    else
                    {
                        string shownType = stackType;
                        if (shownType == null)
                        {
                            shownType = "none";
                        }

                        Plugin.Log.LogInfo(
                            "[world test] sort & stack: skipped (stackable "
                                + $"type {shownType}, {frees.Count} free "
                                + "slots)");
                    }
                }
            }
            else
            {
                Plugin.Log.LogInfo("[world test] no workbench here");
            }

            // Locks: a locked door or chest opened with a lockpick, as the
            // player does it (the lockpick is used up).
            var lck = Nearest(
                Object.FindObjectsOfType<Locked>().Where(
                    l => l.locked
                        && (l.GetComponent<Door>() != null
                            || l.GetComponent<Item>() != null)
                        && ObjRef.Of(l.gameObject).Valid),
                at);
            if (lck == null)
            {
                var near = Nearest(
                    Object.FindObjectsOfType<Door>().Where(
                        d => !d.destroyed && ObjRef.Of(d.gameObject).Valid),
                    at);
                if (near != null)
                {
                    near.lockMe("");
                    lck = near.GetComponent<Locked>();
                    Plugin.Log.LogInfo(
                        "[world test] nothing locked found: locked "
                            + $"{near.name}");
                    yield return new WaitForSeconds(1f);
                }
            }

            if (lck != null)
            {
                string lockName = lck.name;
                ObjRef lockRef = ObjRef.Of(lck.gameObject);
                Vector3 lockAt = lck.transform.position;
                string kind = "item";
                if (lck.GetComponent<Door>() != null)
                {
                    kind = "door";
                }

                Plugin.Log.LogInfo(
                    $"[world test] lock on {lockName} "
                        + $"{lockRef} at {lockAt} ({kind}, key {lck.keyType})");
                player.Inventory.addItemTypeToPlayer("lockpick", 1);
                yield return null;
                lck.tryToLockpick();
                Plugin.Log.LogInfo(
                    $"[world test] after the lockpick: locked={lck.locked}");
                yield return new WaitForSeconds(2f);
            }

            // Other locks, for the authority test: where they are and how
            // they're named.
            foreach (var l in Object.FindObjectsOfType<Locked>()
                .Where(l => l.locked && ObjRef.Of(l.gameObject).Valid)
                .OrderBy(l => (l.transform.position - at).sqrMagnitude).Take(4))
            {
                Plugin.Log.LogInfo(
                    $"[world test] still locked: {l.name} "
                        + $"{ObjRef.Of(l.gameObject)} "
                        + $"at {l.transform.position}");
            }

            // A combination padlock: open its window, set the code's numbers
            // and check it, as the player does.
            var pad = Nearest(
                Object.FindObjectsOfType<Padlock>().Where(
                    pl => pl.locked && ObjRef.Of(pl.gameObject).Valid),
                at);
            if (pad != null && !string.IsNullOrEmpty(pad.combination))
            {
                Plugin.Log.LogInfo(
                    $"[world test] padlock {pad.name} "
                        + $"{ObjRef.Of(pad.gameObject)} "
                        + $"at {pad.transform.position}, "
                        + $"code {pad.combination}");
                pad.activate();
                yield return new WaitForSecondsRealtime(0.5f);
                PadlockNumber[] numbers;
                if (Singleton<UI>.Instance.padlock != null)
                {
                    numbers =
                        Singleton<UI>.Instance.padlock
                            .GetComponentsInChildren<PadlockNumber>();
                }
                else
                {
                    numbers = new PadlockNumber[0];
                }

                for (int i = 0; i < numbers.Length
                    && i < pad.combination.Length; i++)
                {
                    numbers[i].number = pad.combination[i] - '0';
                }

                pad.checkCombination();
                Plugin.Log.LogInfo(
                    "[world test] after entering the code: "
                        + $"locked={pad.locked}");
                yield return new WaitForSecondsRealtime(2f);
            }
            else
            {
                Plugin.Log.LogInfo("[world test] no locked padlock found");
            }

            // A locked crate picked open (the progress bar's success:
            // Item.disarm, which opens it), then let go of again.
            var crate = Object.FindObjectsOfType<Item>().Where(
                i => WorldActions.Crate(i) != null && !i.searched
                    && ObjRef.Of(i.gameObject).Valid)
                .OrderBy(i => (i.transform.position - at).sqrMagnitude)
                .ToList();
            foreach (var c in crate.Skip(1).Take(3))
            {
                Plugin.Log.LogInfo(
                    $"[world test] another locked crate: {c.name} "
                        + $"{ObjRef.Of(c.gameObject)} "
                        + $"at {c.transform.position}");
            }

            if (crate.Count > 0)
            {
                var c = crate[0];
                Plugin.Log.LogInfo(
                    $"[world test] picking crate {c.name} "
                        + $"{ObjRef.Of(c.gameObject)} "
                        + $"at {c.transform.position} "
                        + $"(chance {c.disarmChance})");
                c.disarm();
                Plugin.Log.LogInfo($"[world test] crate searched={c.searched}");
                yield return new WaitForSeconds(1.5f);
                player.openedItemInventory = null;
                yield return new WaitForSeconds(1.5f);
            }
            else
            {
                Plugin.Log.LogInfo("[world test] no locked crate found");
            }

            // Furniture smashed by our player: a hit that leaves it standing,
            // then one that breaks it (one message each, the second with no
            // health left).
            at = player.transform.position;
            var furniture = Nearest(Object.FindObjectsOfType<Item>()
                    .Where(
                        i => i.destructible && !i.destroyed && i.health > 1
                            && !i.hasInventory && i.shadowArmor == null
                            && i.GetComponent<Explodes>() == null
                            && i.GetComponent<Trigger>() == null
                            && i.GetComponent<ProxyItem>() == null
                            && ObjRef.Of(i.gameObject).Valid), at);
            if (furniture == null)
            {
                // None in this save near us: place one as a player places a
                // trap, so the others get it too (ObjSpawn).
                foreach (var prefab in new[]
                {
                    "Furnitures/Cottage/chair_1",
                    "Furnitures/Cottage/table_1",
                    "Furnitures/Cottage/shelf",
                    "Furnitures/Cottage/crate_movable_01",
                    "Furnitures/Cottage/Wardrobe_Big_1"
                })
                {
                    var made =
                        PlaceShared(prefab, at + new Vector3(250f, 0f, 0f));
                    // its health is set in Start
                    yield return null;
                    yield return null;
                    Item it = null;
                    if (made != null)
                    {
                        it = made.GetComponent<Item>();
                    }

                    string itemState;
                    if (it == null)
                    {
                        itemState = "no item";
                    }
                    else
                    {
                        itemState =
                            $"destructible={it.destructible}, health "
                                + $"{it.health} of {it.maxHealth}";
                    }

                    Plugin.Log.LogInfo(
                        $"[world test] placed {prefab}: {itemState}");
                    if (it != null && it.destructible && !it.destroyed
                        && it.health > 1)
                    {
                        furniture = it;
                        Plugin.Log.LogInfo(
                            $"[world test] none in this save: placed {prefab}");
                        break;
                    }

                    if (made != null)
                    {
                        Object.Destroy(made);
                    }
                }

                // its ObjSpawn goes out first
                yield return new WaitForSeconds(1f);
            }

            if (furniture != null)
            {
                Plugin.Log.LogInfo(
                    $"[world test] smashing {furniture.name} "
                        + $"{ObjRef.Of(furniture.gameObject)} "
                        + $"at {furniture.transform.position}: "
                        + $"health {furniture.health}");
                furniture.getHit(furniture.health / 2, player.transform);
                yield return new WaitForSeconds(1f);
                furniture.getHit(furniture.health + 1, player.transform);
                Plugin.Log.LogInfo(
                    $"[world test] {furniture.name} after two hits: health "
                        + $"{furniture.health}, broken={furniture.destroyed}");
                yield return new WaitForSeconds(2f);
                if (furniture != null && furniture.destroyed)
                {
                    // as building it again does (Constructible.construct)
                    furniture.setNormalState();
                    Plugin.Log.LogInfo(
                        $"[world test] {furniture.name} repaired: health "
                            + $"{furniture.health}");
                    yield return new WaitForSeconds(2f);
                }
            }
            else
            {
                Plugin.Log.LogInfo(
                    "[world test] no destructible furniture found");
            }

            // An exploding barrel or gas bottle set off (as a shot does), far
            // enough from our player not to hurt it.
            var explosives = Object.FindObjectsOfType<Explodes>()
                .Where(
                    e => e.GetComponent<ThrownItem>() == null
                        && !BreakSync.Activated(e)).ToList();
            var barrel = Nearest(
                explosives.Where(
                    e => ObjRef.Of(e.gameObject).Valid
                        && Vector3.Distance(e.transform.position, at)
                            > e.radius + 100f),
                at);
            Plugin.Log.LogInfo(
                $"[world test] explosives here: {explosives.Count}");
            if (barrel == null)
            {
                var go = PlaceShared(
                    "Traps/barrelExploding", at + new Vector3(0f, 0f, 900f));
                if (go != null)
                {
                    barrel = go.GetComponentInChildren<Explodes>();
                }
                else
                {
                    barrel = null;
                }

                if (barrel != null)
                {
                    Plugin.Log.LogInfo(
                        "[world test] none in this save: placed one");
                }

                yield return new WaitForSeconds(1f);
            }

            if (barrel != null)
            {
                Plugin.Log.LogInfo(
                    $"[world test] setting off {barrel.name} "
                        + $"{ObjRef.Of(barrel.gameObject)} "
                        + $"at {barrel.transform.position}");
                barrel.onActivate();
                yield return new WaitForSeconds(3f);
            }
            else
            {
                Plugin.Log.LogInfo("[world test] no explosive found");
            }
        }
    }
}
