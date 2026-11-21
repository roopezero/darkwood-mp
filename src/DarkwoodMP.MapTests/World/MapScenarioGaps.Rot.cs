using System.Collections;
using System.Linq;
using HarmonyLib;
using DarkwoodMP.World.Inventories;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    public static partial class MapScenarioGaps
    {
        // ---- 64 rotting food in an open container ---------------------------

        /// <summary>"rot-open" (client): open the setup's wardrobe, let its
        /// food rot by the game's own refresh (Inventory.refreshItems) while
        /// it's open, then take the food. Neither the rot nor the take may be
        /// sent as a change the host refuses.</summary>
        private static IEnumerator RotOpen(string[] a, Result r)
        {
            var p = Player.Instance;
            var inv = RotWardrobe();
            if (inv == null)
            {
                r.Fail("no wardrobe at the rot spot");
                yield break;
            }

            yield return Walk(inv.transform.position);
            p.openedItemInventory = inv; // as if its window were open
            yield return new WaitForSecondsRealtime(
                SlotRules.AskTimeout + 0.5f);
            var slot = inv.slots.FirstOrDefault(
                s => !InvItemClass.isNull(s.invItem)
                    && s.invItem.baseClass != null
                    && s.invItem.baseClass.rottenItem != null);
            if (slot == null)
            {
                r.Fail(
                    $"no rotting food in {inv.name} "
                        + $"{ObjRef.Of(inv.gameObject)}");
                p.openedItemInventory = null;
                yield break;
            }

            var it = slot.invItem;
            string food = it.type; // (the slot's item is cleared when it moves)
            float max = Mathf.Max(1f, it.baseClass.maxDurability),
                d0 = it.durability;
            if (!it.activated)
            {
                // a seen perishable in a container rots
                // (checkForActiveSwitches)
                it.switchActive(true);
            }

            int mark = MapScenarioIssues.LogMark();
            int calls = 0;
            for (int frame = 0; frame < 30
                && !InvItemClass.isNull(slot.invItem)
                && d0 - slot.invItem.durability < 0.02f * max; frame++)
            {
                // An experience item drains only while the clock runs
                // (refreshItems), which a joiner's never does by itself: let it
                // run for these refreshes, as the host's does (TimeSync turns
                // it off again).
                var ctl = Singleton<Controller>.Instance;
                bool was = ctl.DoUpdateTime;
                if (it.baseClass.isExpItem)
                {
                    ctl.DoUpdateTime = true;
                }

                try
                {
                    for (int i = 0; i < 1000 && slot.invItem == it
                        && d0 - it.durability < 0.02f * max; i++)
                    {
                        inv.refreshItems();
                        calls++;
                    }
                }
                finally
                {
                    ctl.DoUpdateTime = was;
                }

                yield return null;
            }

            float d1;
            if (InvItemClass.isNull(slot.invItem))
            {
                d1 = -1f;
            }
            else
            {
                d1 = slot.invItem.durability;
            }

            // a few of Containers' looks at the open slots
            yield return new WaitForSecondsRealtime(1.5f);
            var sent = LogSince(mark)
                .Where(
                    l => l.Contains($"slot(s) of {inv.name}")
                        && l.Contains("sent") && !l.Contains("[map]")).ToList();
            var refused = Refused(mark);
            r.Info =
                $"{it.type} durability {d0:0.###} -> {d1:0.###} of {max:0.##} "
                    + $"in {calls} refreshes";
            if (d1 < 0f || d1 >= d0)
            {
                r.Fail($"it didn't rot: {r.Info}");
                p.openedItemInventory = null;
                yield break;
            }

            if (sent.Count > 0)
            {
                r.Fail(
                    $"the rot was sent as our change: {sent[0].Trim()}; "
                        + $"{r.Info}");
            }

            if (refused.Count > 0)
            {
                r.Fail($"refused: {refused[0].Trim()}; {r.Info}");
            }

            int carried0 = p.Inventory.slots.Concat(p.Hotbar.slots).Count(
                x => x != null && !InvItemClass.isNull(x.invItem)
                    && x.invItem.type == food);
            mark = MapScenarioIssues.LogMark();
            AsPlayer(() => slot.transferItemAllToPlayer());
            yield return new WaitForSecondsRealtime(2f);
            p.openedItemInventory = null;
            yield return new WaitForSecondsRealtime(0.5f);
            refused = Refused(mark);
            int carried1 = p.Inventory.slots.Concat(p.Hotbar.slots).Count(
                x => x != null && !InvItemClass.isNull(x.invItem)
                    && x.invItem.type == food);
            r.Info += $"; carried {carried0} -> {carried1}";
            if (refused.Count > 0)
            {
                r.Fail($"the take was refused: {refused[0].Trim()}; {r.Info}");
            }
            else if (carried1 <= carried0 || !InvItemClass.isNull(slot.invItem))
            {
                r.Fail($"the food didn't come into our bags: {r.Info}");
            }
        }

        /// <summary>
        /// "rot-host" (host): the client's take came through (no food left in
        /// the wardrobe) and we refused nothing of it.
        /// </summary>
        private static IEnumerator RotHost(string[] a, Result r)
        {
            var inv = RotWardrobe();
            if (inv == null)
            {
                r.Fail("no wardrobe at the rot spot");
                yield break;
            }

            var left = inv.slots.Where(s => !InvItemClass.isNull(s.invItem))
                .Select(s => $"{s.invItem.type}x{s.invItem.amount}").ToList();
            var refused = MapScenarioIssues.LogLines()
                .Where(l => l.Contains($"of {inv.name}: refused")).ToList();
            r.Info =
                $"{inv.name} "
                    + $"{ObjRef.Of(inv.gameObject)}: "
                    + $"[{string.Join(",", left)}]";
            if (refused.Count > 0)
            {
                r.Fail($"refused: {refused[0].Trim()}");
            }
            else if (left.Count > 0)
            {
                r.Fail($"the food is still here: {r.Info}");
            }
        }
    }
}
