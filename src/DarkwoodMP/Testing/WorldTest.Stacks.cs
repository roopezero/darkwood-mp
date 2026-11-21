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
    /// The world test's stack and hideout steps (WorldTest).
    /// </summary>
    public static partial class WorldTest
    {
        /// <summary>
        /// World.StackSizes at 50: 30 bandages picked up one by one go in one
        /// slot; Sort &amp; Stack makes one stack of 3 + 3 + 40 nails in the
        /// workbench's storage and pours gasoline cans at 10%, 15% and 20% into
        /// one at 45%; two gasoline cans picked up stay two (their amount is
        /// fuel); with the setting at 0 a bandage stacks to the game's own size
        /// again.
        /// </summary>
        private static IEnumerator StacksStep(Player player, Vector3 at)
        {
            var cfg = Plugin.Cfg.StackSize;
            int setting = cfg.Value;
            if (setting != 50)
            {
                cfg.Value = 50;
            }

            yield return null;
            void Check(bool ok, string what)
            {
                string verdict = "FAIL";
                if (ok)
                {
                    verdict = "PASS";
                }

                Plugin.Log.LogInfo($"[world test] stacks: {verdict} {what}");
            }

            var db = DbItems().ToList();
            foreach (var (k, it) in db.Where(
                x => x.it.stacksDurability
                    || (x.it.stackable && x.it.maxAmount <= 1)))
            {
                Plugin.Log.LogInfo(
                    $"[world test] stacks: left alone {k} (stackable "
                        + $"{it.stackable}, stacksDurability "
                        + $"{it.stacksDurability}, maxAmount {it.maxAmount})");
            }

            string bandage =
                db.Select(x => x.key).FirstOrDefault(k => k == "bandage");
            if (bandage == null)
            {
                bandage = db.Select(x => x.key).FirstOrDefault(
                    k => k.Contains("bandage"));
            }

            string gas = db.Where(x => x.it.stacksDurability)
                .Select(x => x.key).FirstOrDefault(k => k.Contains("gas"));
            if (gas == null)
            {
                gas = db.Where(x => x.it.stacksDurability).Select(x => x.key)
                    .FirstOrDefault();
            }

            List<InvSlot> Bags()
            {
                List<InvSlot> slots = player.Inventory.slots;
                List<InvSlot> hotbar;
                if (player.Hotbar != null)
                {
                    hotbar = player.Hotbar.slots;
                }
                else
                {
                    hotbar = new List<InvSlot>();
                }

                return slots.Concat(hotbar).ToList();
            }

            void Clear(string type)
            {
                foreach (var sl in Bags())
                {
                    if (!InvItemClass.isNull(sl.invItem)
                        && sl.invItem.type == type)
                    {
                        sl.removeItem();
                    }
                }
            }

            List<int> Stacks(string type) => Bags()
                .Where(
                    sl => !InvItemClass.isNull(sl.invItem)
                        && sl.invItem.type == type)
                .Select(sl => sl.invItem.amount).ToList();

            if (bandage != null)
            {
                Clear(bandage);
                for (int i = 0; i < 30; i++)
                {
                    player.Inventory.addItemTypeToPlayer(bandage, 1);
                }

                yield return null;
                var st = Stacks(bandage);
                Check(
                    st.Count == 1 && st[0] == 30,
                    $"30 {bandage} picked up one by one: "
                        + $"{string.Join(", ", st.Select(a => "x" + a))} "
                        + "(game's own size "
                        + $"{GameSize(bandage)})");
                Clear(bandage);
            }
            else
            {
                Check(false, "no bandage in the items database");
            }

            if (gas != null)
            {
                Clear(gas);
                player.Inventory.addItemTypeToPlayer(gas, 1);
                player.Inventory.addItemTypeToPlayer(gas, 1);
                yield return null;
                var st = Stacks(gas);
                var b = Singleton<ItemsDatabase>.Instance.getItem(
                    gas, instantiate: false);
                Check(
                    st.Count == 2,
                    $"two {gas} stay two: {st.Count} slot(s) (stackable "
                        + $"{b.stackable}, maxAmount {b.maxAmount}, "
                        + $"stacksDurability {b.stacksDurability})");
                Clear(gas);
            }

            // Sort & Stack in the workbench's storage: 3 + 3 + 40 nails -> 46.
            var wb = Nearest(Object.FindObjectsOfType<Workbench>(), at);
            Inventory storage = null;
            if (wb != null)
            {
                storage = wb.normalInventory;
            }

            var frees = new List<int>();
            void ClearNails()
            {
                foreach (var sl in storage.slots)
                {
                    if (!InvItemClass.isNull(sl.invItem)
                        && sl.invItem.type == "nail")
                    {
                        sl.inventory = storage;
                        sl.removeItem();
                    }
                }
            }

            if (storage != null)
            {
                for (int i = 0; i < storage.slots.Count; i++)
                {
                    if (InvItemClass.isNull(storage.slots[i].invItem))
                    {
                        frees.Add(i);
                    }
                }
            }

            if (storage != null && frees.Count >= 3
                && db.Any(x => x.key == "nail"))
            {
                var by = wb.transform.position
                    + (player.transform.position
                        - wb.transform.position).normalized * 120f;
                player.transform.position =
                    new Vector3(by.x, player.transform.position.y, by.z);
                yield return new WaitForSeconds(1f);
                player.openedItemInventory2 = storage;
                yield return new WaitForSeconds(0.6f);
                ClearNails();
                frees.Clear();
                for (int i = 0; i < storage.slots.Count; i++)
                {
                    if (InvItemClass.isNull(storage.slots[i].invItem))
                    {
                        frees.Add(i);
                    }
                }

                int[] amounts = { 3, 3, 40 };
                for (int j = 0; j < 3; j++)
                {
                    var sl = storage.slots[frees[j * (frees.Count - 1) / 2]];
                    sl.inventory = storage;
                    sl.createItem("nail", amounts[j]);
                }

                yield return new WaitForSeconds(0.8f);
                CoopSession sorting = Coop.Current;
                if (sorting != null)
                {
                    sorting.Slots.SortPressed(storage);
                }

                yield return new WaitForSeconds(0.8f);
                var nails = storage.slots.Where(
                    sl => !InvItemClass.isNull(sl.invItem)
                        && sl.invItem.type == "nail")
                    .Select(sl => sl.invItem.amount).ToList();
                Check(
                    nails.Count == 1 && nails[0] == 46,
                    "sort & stack of 3 + 3 + 40 nails: "
                        + $"{string.Join(", ", nails.Select(a => "x" + a))}");
                ClearNails();
                yield return new WaitForSeconds(0.8f);

                // Gasoline cans at 10%, 15% and 20%: poured into one can at
                // 45%, the other two gone.
                if (gas != null)
                {
                    float max = Singleton<ItemsDatabase>.Instance.getItem(
                        gas, instantiate: false).maxDurability;
                    void ClearGas()
                    {
                        foreach (var sl in storage.slots)
                        {
                            if (!InvItemClass.isNull(sl.invItem)
                                && sl.invItem.type == gas)
                            {
                                sl.inventory = storage;
                                sl.removeItem();
                            }
                        }
                    }

                    ClearGas();
                    frees.Clear();
                    for (int i = 0; i < storage.slots.Count; i++)
                    {
                        if (InvItemClass.isNull(storage.slots[i].invItem))
                        {
                            frees.Add(i);
                        }
                    }

                    float[] parts = { 0.10f, 0.15f, 0.20f };
                    for (int j = 0; j < 3; j++)
                    {
                        var sl =
                            storage.slots[frees[j * (frees.Count - 1) / 2]];
                        sl.inventory = storage;
                        sl.createItem(gas, 1);
                        sl.invItem.durability = max * parts[j];
                        sl.invItem.refresh();
                    }

                    yield return new WaitForSeconds(0.8f);
                    CoopSession pouring = Coop.Current;
                    if (pouring != null)
                    {
                        pouring.Slots.SortPressed(storage);
                    }

                    yield return new WaitForSeconds(0.8f);
                    var cans = storage.slots.Where(
                        sl => !InvItemClass.isNull(sl.invItem)
                            && sl.invItem.type == gas)
                        .Select(sl => sl.invItem.durability).ToList();
                    Check(
                        cans.Count == 1
                            && Mathf.Abs(cans[0] - max * 0.45f) < 0.01f * max,
                        $"sort & stack of {gas} at 10% + 15% + 20% of {max}: "
                            + string.Join(
                                ", ", cans.Select(d => d.ToString("0.#"))));
                    ClearGas();
                    yield return new WaitForSeconds(0.8f);
                }

                player.openedItemInventory2 = null;
            }
            else
            {
                Check(
                    false,
                    "sort & stack of nails: no workbench storage with 3 free "
                        + $"slots ({frees.Count} free)");
            }

            // 0: the game's own sizes again.
            cfg.Value = 0;
            yield return null;
            if (bandage != null)
            {
                int max = Singleton<ItemsDatabase>.Instance.getItem(
                    bandage, instantiate: false).maxAmount;
                Check(
                    max == GameSize(bandage) && max < 30,
                    $"StackSize 0: {bandage} stacks to {max} again");
            }

            cfg.Value = setting;
            yield return null;
        }

        /// <summary>
        /// World.Hideouts: an oven lit by the host's player (OvenLit; the old
        /// one stays on, as the observer has it as theirs), the porter asked
        /// while at the hideout to take from (nothing emptied), and the
        /// porter's delivery from another hideout to this one
        /// (PorterDelivered).
        /// </summary>
        private static IEnumerator HideoutsStep(Player player)
        {
            var ovens = Object.FindObjectsOfType<ExperienceMachine>();
            foreach (var o in ovens)
            {
                var l = o.transform.GetLocation();
                string ovenName = o.name;
                ObjRef ovenRef = ObjRef.Of(o.gameObject);
                bool ovenOn = o.isOn;
                string location = "no location";
                if (l != null)
                {
                    location = l.name + " hideout " + l.hideoutId + " ward "
                        + (l.shadowWard != null && l.shadowWard.activeSelf);
                }

                string ours = "";
                if (o == player.experienceMachine)
                {
                    ours = " (ours)";
                }

                Plugin.Log.LogInfo(
                    $"[world test] oven {ovenName} {ovenRef} on {ovenOn} at "
                        + $"{location}{ours}");
            }

            var mine = player.experienceMachine;
            ExperienceMachine other = ovens.FirstOrDefault(o => o != mine);
            if (other == null)
            {
                other = mine;
            }

            if (other != null)
            {
                player.setExperienceMachine(other);
                var l = other.transform.GetLocation();
                string litName = other.name;
                bool litOn = other.isOn;
                bool ward = l != null && l.shadowWard != null
                    && l.shadowWard.activeSelf;
                bool isOurs = player.experienceMachine == other;
                string oldOne = "none";
                if (mine != null)
                {
                    oldOne = mine.name + " on " + mine.isOn;
                }

                Plugin.Log.LogInfo(
                    $"[world test] lit oven {litName}: on {litOn}, ward "
                        + $"{ward}, ours {isOurs}; the old one {oldOne}");
                yield return new WaitForSeconds(1f);
                if (mine != null && mine != other)
                {
                    player.experienceMachine = mine;
                }
            }
            else
            {
                Plugin.Log.LogInfo("[world test] no oven here");
            }

            var hideouts = Object.FindObjectsOfType<Location>()
                .Where(l => l.hideoutId > 0 && !l.isSubLocation)
                .OrderBy(l => l.hideoutId).ToList();
            player.whereAmI.checkWhereAmI();
            var here = player.whereAmI.bigLocation;
            string listing = string.Join(
                ", ",
                hideouts.Select(
                    l => $"{l.name} #{l.hideoutId} ({l.inventoriesList.Count} "
                        + "containers, delivery "
                        + $"{(l.porterDeliveryPosition != null)})"));
            string place = "none";
            if (here != null)
            {
                place = here.name;
            }

            Plugin.Log.LogInfo(
                $"[world test] hideouts: {listing}; we're at {place}");
            if (here != null && hideouts.Contains(here))
            {
                int before = ItemsIn(here);
                here.transportAllItemsToCurrentHideout();
                Plugin.Log.LogInfo(
                    "[world test] porter asked at the hideout to take from: "
                        + $"{before} items there before, {ItemsIn(here)} "
                        + "after");
            }

            var src = hideouts.FirstOrDefault(l => l != here);
            Location dest;
            if (here != null && here.playerBase && here.hideoutId > 0)
            {
                dest = here;
            }
            else
            {
                dest = hideouts.FirstOrDefault(l => l != src);
            }

            if (src != null && dest != null
                && dest.porterDeliveryPosition != null)
            {
                var chest = src.inventoriesList.Select(i =>
                {
                    if (i != null)
                    {
                        return i.GetComponent<Inventory>();
                    }

                    return null;
                }).FirstOrDefault(i => i != null && !i.transform.isLocked());

                if (chest != null)
                {
                    chest.addItemType("bottle", 1);
                }

                Plugin.Log.LogInfo(
                    $"[world test] porter from {src.name} ({ItemsIn(src)} "
                        + $"items) to {dest.name}");
                CoopSession coop = Coop.Current;
                if (coop != null)
                {
                    coop.Porter.Deliver(src, dest);
                }

                Plugin.Log.LogInfo(
                    $"[world test] {src.name} has {ItemsIn(src)} items after");
                yield return new WaitForSeconds(1f);
            }
            else
            {
                Plugin.Log.LogInfo(
                    "[world test] no two hideouts for the porter");
            }
        }

        private static int ItemsIn(Location l) =>
            l.inventoriesList.Sum(i =>
            {
                if (i != null && i.GetComponent<Inventory>() != null
                    && !i.transform.isLocked())
                {
                    return i.GetComponent<Inventory>().getAllItems().Count;
                }

                return 0;
            });

        private static string Parts(GameObject go)
        {
            if (go == null)
            {
                return "none";
            }

            return string.Join(
                ",",
                go.GetComponents<Component>().Select(c => c.GetType().Name));
        }
    }
}
