using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DarkwoodMP.World.Inventories;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>Issue #4: workbench storage.</summary>
    public static partial class MapScenarioIssues
    {
        // ---- #4 workbench storage -------------------------------------------

        private static readonly string[] WbItems = { "nail", "wire", "rope" };

        private static int Amount(Inventory inv, string type) =>
            inv.slots.Where(
                s => !InvItemClass.isNull(s.invItem)
                    && s.invItem.type == type).Sum(s => s.invItem.amount);

        private static Workbench Bench() => Nearest(
            Object.FindObjectsOfType<Workbench>().Where(
                w => w.normalInventory != null),
            Centre);

        /// <summary>"wb-fill" (host, before the join): put things in the
        /// workbench's storage, and change them again once a player has begun
        /// to join (while their game loads the snapshot, which doesn't have
        /// that change).</summary>
        private static IEnumerator WbFill(string[] a, Result r)
        {
            var p = Player.Instance;
            var wb = Bench();
            if (wb == null)
            {
                r.Fail("no workbench");
                yield break;
            }

            var st = wb.normalInventory;
            yield return Walk(wb.transform.position);
            p.openedItemInventory2 = st;
            yield return new WaitForSeconds(0.6f);
            var free = Enumerable.Range(0, st.slots.Count)
                .Where(i => InvItemClass.isNull(st.slots[i].invItem))
                .Take(WbItems.Length).ToList();
            if (free.Count < WbItems.Length)
            {
                p.openedItemInventory2 = null;
                r.Fail($"only {free.Count} free storage slots");
                yield break;
            }

            for (int i = 0; i < WbItems.Length; i++)
            {
                st.slots[free[i]].inventory = st;
                st.slots[free[i]].createItem(WbItems[i], 3);
            }

            yield return new WaitForSeconds(0.6f);
            p.openedItemInventory2 = null;
            Plugin.Instance.StartCoroutine(WbLateChange(wb, free));
            string items = string.Join(
                ", ",
                WbItems.Select((t, i) => $"{free[i]}={t} x3"));
            r.Info =
                $"{st.name} "
                    + $"{ObjRef.Of(st.gameObject)}: {items}; "
                    + "a change follows once a player joins";
        }

        private static IEnumerator WbLateChange(Workbench wb, List<int> slots)
        {
            float until = Time.realtimeSinceStartup + 240f;
            while (Time.realtimeSinceStartup < until
                && (Plugin.Instance.Host.Session.Peers.Count == 0))
            {
                yield return null;
            }

            // the snapshot went with the Welcome; their game is loading it now
            yield return new WaitForSecondsRealtime(2f);
            var p = Player.Instance;
            if (wb == null || p == null)
            {
                yield break;
            }

            var st = wb.normalInventory;
            p.openedItemInventory2 = st;
            yield return new WaitForSeconds(0.6f);
            // wire gone
            st.slots[slots[1]].removeItem();
            st.slots[slots[2]].removeItem();
            // rope 3 -> 5
            st.slots[slots[2]].inventory = st;
            st.slots[slots[2]].createItem("rope", 5);
            yield return new WaitForSeconds(0.6f);
            p.openedItemInventory2 = null;
            Plugin.Log.LogInfo(
                "[map] wb-fill: changed the storage while a player joins "
                    + $"(slot {slots[1]} emptied, slot {slots[2]} rope x5)");
        }

        /// <summary>"wb-use" (client): the storage is the host's (asked for
        /// once our world loaded); open the workbench as Workbench.open does,
        /// shift-click something in (InvSlot.transferItem) and one stack out;
        /// nothing refused or dropped.</summary>
        private static IEnumerator WbUse(string[] a, Result r)
        {
            var p = Player.Instance;
            var wb = Bench();
            if (wb == null)
            {
                r.Fail("no workbench");
                yield break;
            }

            var st = wb.normalInventory;
            var sref = ObjRef.Of(st.gameObject);
            var missing = new List<string>();
            yield return WaitLog(
                0, new[] { Regex.Escape($"[slots] {sref} contents from host") },
                3f, missing);
            if (missing.Count > 0)
            {
                r.Fail(
                    $"no '[slots] {sref} contents from host' in the log: the "
                        + "storage wasn't asked for after joining");
                yield break;
            }

            string got = LogLines().Last(
                l => l.Contains($"[slots] {sref} contents from host"));
            got =
                got.Substring(got.IndexOf("[slots]", StringComparison.Ordinal));
            string Contents() => string.Join(
                ",",
                st.slots.Where(s => !InvItemClass.isNull(s.invItem)).Select(
                    s => $"{s.invItem.type}x{s.invItem.amount}"));
            string before = Contents();
            if (st.slots.Any(
                s => !InvItemClass.isNull(s.invItem)
                    && s.invItem.type == "wire")
                || !st.slots.Any(
                    s => !InvItemClass.isNull(s.invItem)
                        && s.invItem.type == "rope" && s.invItem.amount == 5))
            {
                r.Fail(
                    "the storage here isn't the host's after its late change "
                        + $"(wire gone, rope x5): {before}; {got}");
                yield break;
            }

            int dropped0 = Object.FindObjectsOfType<Inventory>().Count(
                i => i.name.StartsWith(
                    "DroppedItem", StringComparison.Ordinal));
            yield return Walk(wb.transform.position);
            // What we carried before (the trade before this step can have
            // bought a nail).
            int nails0 = Carried(p).Where(i => i.type == "nail")
                .Sum(i => i.amount),
                bandages0 = Carried(p).Where(i => i.type == "bandages")
                    .Sum(i => i.amount);
            // Something of ours to put in (a new character has empty bags).
            var bag = p.Inventory.slots.FirstOrDefault(
                s => InvItemClass.isNull(s.invItem));
            if (bag == null)
            {
                r.Fail("no free slot in our bags");
                yield break;
            }

            bag.inventory = p.Inventory;
            bag.createItem("bandages", 2);
            yield return new WaitForSeconds(0.5f);
            int mark = LogMark();
            AsPlayer(() => wb.open());
            // it asks the host again as it opens; changes wait for the answer
            yield return new WaitForSeconds(SlotRules.AskTimeout + 0.5f);
            if (p.openedItemInventory2 != st)
            {
                r.Fail("the workbench didn't open its storage");
                yield break;
            }

            // shift-click into the storage
            AsPlayer(() => bag.transferItem());
            yield return new WaitForSeconds(0.8f);
            var nails = st.slots.FirstOrDefault(
                s => !InvItemClass.isNull(s.invItem)
                    && s.invItem.type == "nail");
            if (nails != null)
            {
                AsPlayer(() => nails.transferItem());   // and one stack out
            }

            yield return new WaitForSeconds(0.8f);
            try
            {
                p.closeInventory();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"[map] wb-use: closeInventory: {e.Message}");
            }

            if (p.openedItemInventory2 == st)
            {
                p.openedItemInventory2 = null;
            }

            yield return new WaitForSeconds(1.5f);
            string after = Contents();
            int dropped1 = Object.FindObjectsOfType<Inventory>().Count(
                i => i.name.StartsWith(
                    "DroppedItem", StringComparison.Ordinal));
            var refused = LogLines().Skip(mark)
                .Where(l => l.Contains("refused")).ToList();
            // A shift-click moves one of the stack (InvSlot.transferItem;
            // transferItemAll is the whole stack).
            bool inStorage = Amount(st, "bandages") == 1
                && Carried(p).Where(i => i.type == "bandages")
                    .Sum(i => i.amount) == bandages0 + 1;
            bool tookNails = Carried(p).Where(i => i.type == "nail")
                .Sum(i => i.amount) == nails0 + 1 && Amount(st, "nail") == 2;
            r.Info = $"{sref} before [{before}] after [{after}]; {got}";
            if (refused.Count > 0)
            {
                r.Fail($"refused: {refused[0]}");
            }
            else if (!inStorage)
            {
                r.Fail($"the bandages didn't go into the storage: {r.Info}");
            }
            else if (!tookNails)
            {
                r.Fail($"the nails didn't come into our bags: {r.Info}");
            }
            else if (dropped1 != dropped0)
            {
                r.Fail($"dropped items {dropped0} -> {dropped1}: {r.Info}");
            }
        }

        /// <summary>
        /// "wb-host" (host): the client's two changes (a bandage in, a nail
        /// out) are in our storage, and we refused none of them.
        /// </summary>
        private static IEnumerator WbHost(string[] a, Result r)
        {
            yield return new WaitForSeconds(0.5f);
            var wb = Bench();
            if (wb == null)
            {
                r.Fail("no workbench");
                yield break;
            }

            var st = wb.normalInventory;
            string contents = string.Join(
                ",",
                st.slots.Where(s => !InvItemClass.isNull(s.invItem)).Select(
                    s => $"{s.invItem.type}x{s.invItem.amount}"));
            var refused = LogLines()
                .Where(l => l.Contains($"of {st.name}: refused")).ToList();
            r.Info = $"{st.name}: [{contents}]";
            if (refused.Count > 0)
            {
                r.Fail($"refused: {refused[0]}");
            }
            else if (Amount(st, "bandages") != 1 || Amount(st, "nail") != 2)
            {
                r.Fail($"the client's changes aren't here: {r.Info}");
            }
        }
    }
}
