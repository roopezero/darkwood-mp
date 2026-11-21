using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>Issues #1, #13 and #3: joining.</summary>
    public static partial class MapScenarioIssues
    {
        // ---- #1 loading screen while joining --------------------------------

        /// <summary>
        /// "join-log" (client): the join showed our loading screen, its stages,
        /// and handed over to the game's.
        /// </summary>
        private static IEnumerator JoinLog(string[] a, Result r)
        {
            var missing = new List<string>();
            yield return WaitLog(0, new[]
            {
                @"\[join\] loading screen shown",
                @"\[join\] loading screen: "
                    + @"(download|load|connect|wait)\w* \d+%",
                @"\[join\] loading screen: (handed over to the game's"
                    + @"|the game's loading has it now)"
            }, 6f, missing);

            var lines = LogLines();
            var join = lines.Where(l => l.Contains("[join]"))
                .Select(
                    l => l.Substring(
                        l.IndexOf("[join]", StringComparison.Ordinal)))
                .ToList();
            var bad = join.FirstOrDefault(
                l => l.Contains("didn't show") || l.Contains("hidden:"));
            r.Info = $"{join.Count} [join] lines: " + string.Join(
                " | ", join.Distinct().Take(6));
            if (missing.Count > 0)
            {
                r.Fail(
                    $"not in the log: {string.Join(", ", missing)} (saw: "
                        + $"{string.Join(" | ", join.Distinct().Take(8))})");
            }
            else if (bad != null)
            {
                r.Fail($"the loading screen fell back: {bad}");
            }
        }

        // ---- #13 joining players placed apart -------------------------------

        /// <summary>
        /// "join-apart" (client, just after joining): our player isn't
        /// overlapping any other player, the log says where it was placed
        /// ([spawn]), and the others' puppets are seen where they are.
        /// </summary>
        private static IEnumerator JoinApart(string[] a, Result r)
        {
            var p = Player.Instance;
            if (p == null)
            {
                r.Fail("no player");
                yield break;
            }

            var ps = Sync.PlayerSync.Instance;
            float until = Time.realtimeSinceStartup + 5f;
            while ((ps == null || !ps.Remotes.Values.Any(
                x => x.Owner != Sync.PlayerSync.DummyId && x.Last != null))
                && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            // touching is fine, overlapping isn't
            float apart = Sync.SpawnSpot.Apart(p) - 10f;
            var parts = new List<string>();
            if (ps != null)
            {
                foreach (var rp in ps.Remotes.Values)
                {
                    if (rp.Owner == Sync.PlayerSync.DummyId || rp.Last == null)
                    {
                        continue;
                    }

                    var o = rp.Last.Position;
                    var me = p.transform.position;
                    float d = new Vector2(o.x - me.x, o.z - me.z).magnitude;
                    parts.Add($"{rp.Name} {d:0} away");
                    if (d < apart)
                    {
                        r.Fail(
                            $"we overlap {rp.Name}: {d:0} apart, bodies need "
                                + $"{apart:0}");
                    }
                }
            }

            if (parts.Count == 0)
            {
                r.Fail("no other player seen");
            }

            // The log file is written a little behind (this step runs right
            // after the join): wait for the line.
            var missing = new List<string>();
            yield return WaitLog(0, new[] { @"\[spawn\]" }, 8f, missing);
            var spawn = LogLines().LastOrDefault(l => l.Contains("[spawn]"));
            if (spawn == null)
            {
                r.Fail("no [spawn] line in the log");
            }

            string spawnNote = "";
            if (spawn != null)
            {
                spawnNote = " | " + spawn.Substring(
                    spawn.IndexOf("[spawn]", StringComparison.Ordinal));
            }

            r.Info = string.Join(", ", parts) + spawnNote;
        }

        // ---- #3 re-equip after join -----------------------------------------

        /// <summary>
        /// "hold TYPE" (host, before the join): TYPE in a hotbar slot, that
        /// slot selected and in our hands.
        /// </summary>
        private static IEnumerator Hold(string[] a, Result r)
        {
            var p = Player.Instance;
            string type = "pitchfork";
            if (a.Length > 0)
            {
                type = a[0];
            }

            int slot = p.Hotbar.slots.FindIndex(
                s => !InvItemClass.isNull(s.invItem) && s.invItem.type == type);
            if (slot < 0)
            {
                slot = p.Hotbar.slots.FindIndex(
                    s => InvItemClass.isNull(s.invItem));
                if (slot < 0)
                {
                    slot = 0;
                }

                var inBag = p.Inventory.slots.FirstOrDefault(
                    s => !InvItemClass.isNull(s.invItem)
                        && s.invItem.type == type);
                InvItemClass it = null;
                if (inBag != null)
                {
                    it = new InvItemClass(inBag.invItem);
                }

                if (inBag != null)
                {
                    inBag.removeItem();
                }

                if (!InvItemClass.isNull(p.Hotbar.slots[slot].invItem))
                {
                    p.Hotbar.slots[slot].removeItem();
                }

                p.Hotbar.slots[slot].inventory = p.Hotbar;
                if (it != null)
                {
                    p.Hotbar.slots[slot].createItem(it);
                }
                else
                {
                    p.Hotbar.slots[slot].createItem(type, 1);
                }
            }

            // Inventory.selectSlot does nothing for the slot that's already
            // selected (an empty slot 0, say, which we just filled), so select
            // it the way Characters.Reequip does: InvSlot.select, through
            // switchToItem.
            if (p.Hotbar.slots[slot].selected)
            {
                p.selectCurrentHotbarSlot();
            }
            else
            {
                p.Hotbar.selectSlot(slot, noiseless: true, force: true);
            }

            float until = Time.realtimeSinceStartup + 5f;
            while ((InvItemClass.isNull(p.currentItem)
                    || p.currentItem.type != type)
                && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            yield return new WaitForSeconds(0.5f);
            r.Info =
                $"hotbar slot {slot}: "
                    + $"{ItemName(p.Hotbar.slots[slot].invItem)}, holding "
                    + $"{ItemName(p.currentItem)}";
            if (InvItemClass.isNull(p.currentItem)
                || p.currentItem.type != type)
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>"check-equip" (client): what we hold is the selected hotbar
        /// slot's item (bare hands if it's empty), not the host's item from the
        /// save, and the log says it was re-equipped.</summary>
        private static IEnumerator CheckEquip(string[] a, Result r)
        {
            var p = Player.Instance;
            var missing = new List<string>();
            yield return WaitLog(
                0, new[] { @"\[chars\] re-equipped" }, 3f, missing);
            var slot = p.getCurrentlySelectedHotbarSlot();
            InvItemClass want;
            if (slot != null && !InvItemClass.isNull(slot.invItem))
            {
                want = slot.invItem;
            }
            else
            {
                want = p.fists;
            }

            var held = p.currentItem;
            string line = LogLines().LastOrDefault(
                l => l.Contains("[chars] re-equipped"));
            string heldName = ItemName(held);
            string bare = "";
            if (held == p.fists)
            {
                bare = " (bare hands)";
            }

            InvItemClass slotItem = null;
            if (slot != null)
            {
                slotItem = slot.invItem;
            }

            string slotName = ItemName(slotItem);
            string logged = null;
            if (line != null)
            {
                logged = line.Substring(
                    line.IndexOf("[chars]", StringComparison.Ordinal));
            }

            r.Info =
                $"holding {heldName}{bare}, selected slot holds {slotName}; "
                    + $"{logged}";
            if (missing.Count > 0)
            {
                r.Fail("no '[chars] re-equipped' in the log");
            }
            else if (held != want
                && !(InvItemClass.isNull(held) && want == p.fists))
            {
                string item = ItemName(held);
                string place = "in no slot: the host's from the save";
                if (InBags(p, held))
                {
                    place = "in our bags";
                }

                r.Fail($"holding {item} ({place}), not {ItemName(want)}");
            }
        }
    }
}
