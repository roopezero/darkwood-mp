using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.World.Workbenches;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World.Inventories
{
    /// <summary>
    /// SlotShare: reading and setting one slot (its key and JSON), the facts
    /// the rules look at, and the world's own drift.
    /// </summary>
    internal sealed partial class SlotShare
    {
        // ---- slots ----------------------------------------------------------

        private static string Key(InvSlot s) => Key(s.invItem);

        private static string Key(InvItemClass it) =>
            SlotKeyRules.Of(FactsOf(it));

        /// <summary>
        /// What the slot rules look at in an item (SlotItem; the zero value
        /// when the slot is empty).
        /// </summary>
        private static SlotItem FactsOf(InvItemClass it)
        {
            if (InvItemClass.isNull(it))
            {
                return default;
            }

            int upgrades = 0;
            if (it.upgrades != null)
            {
                upgrades = it.upgrades.Count;
            }

            var item = new SlotItem
            {
                Filled = true,
                Type = it.type,
                Amount = it.amount,
                Durability = it.durability,
                Recipe = it.isRecipe,
                Upgrades = upgrades,
            };

            var basic = it.baseClass;
            if (basic == null)
            {
                return item;
            }

            return item with
            {
                HasBase = true,
                Rots = basic.rottenItem != null,
                Regenerates = basic.regeneratesWhenInactive,
                Drain = basic.durabilityDrain,
                MaxDurability = basic.maxDurability,
            };
        }

        /// <summary>
        /// What the slot rules look at in slot i here against seen, the keys
        /// of our last look (null when not watched). Seen is false for a slot
        /// past either; HostKey is left for the caller.
        /// </summary>
        private static SlotFacts FactsOf(Inventory inv, int i, string[] seen)
        {
            if (i >= inv.slots.Count)
            {
                return default;
            }

            var here = inv.slots[i].invItem;
            var slot = new SlotFacts
            {
                NowKey = Key(here),
                Here = FactsOf(here),
            };

            if (seen == null || i >= seen.Length)
            {
                return slot;
            }

            return slot with
            {
                Seen = true,
                SeenKey = seen[i],
            };
        }

        /// The keys of our last look at a container, or null.
        private string[] Watched(Inventory inv)
        {
            _watched.TryGetValue(inv, out var keys);
            return keys;
        }

        /// <summary>
        /// Host: start the rot of perishables in a container a client opened,
        /// as drawing its slots here would.
        /// </summary>
        private static void StartRot(Inventory inv)
        {
            var ctl = Singleton<Controller>.Instance;
            if (ctl == null)
            {
                return;
            }

            bool any = false;
            foreach (var slot in inv.slots)
            {
                InvItemClass it = null;
                if (slot != null)
                {
                    it = slot.invItem;
                }

                if (InvItemClass.isNull(it) || it.baseClass == null
                    || it.baseClass.rottenItem == null)
                {
                    continue;
                }

                any = true;
                if (it.timeSeen != 0)
                {
                    continue;
                }

                it.timeSeen = ctl.totalTime;
                if (!it.activated)
                {
                    // what checkForActiveSwitches does for a seen perishable in
                    // a container
                    it.switchActive(true);
                }
            }

            if (any)
            {
                inv.RefreshingItems = true;
            }
        }

        /// <summary>
        /// Keys of a watched storage just before the world's own refresh
        /// (Inventory.refreshItems), or null.
        /// </summary>
        internal string[] BeforeDrift(Inventory inv)
        {
            if (inv != null && _watched.ContainsKey(inv))
            {
                return Keys(inv);
            }

            return null;
        }

        /// <summary>
        /// After the world's own refresh: a slot that changed in it (rot,
        /// drain, recharge) and that the player hadn't changed since the last
        /// Check takes its new key as the starting point, so it isn't sent.
        /// </summary>
        internal void Drift(Inventory inv, string[] before)
        {
            if (before == null || inv == null
                || !_watched.TryGetValue(inv, out var keys))
            {
                return;
            }

            // in place: a Merge under way may hold this array as what it saw
            var kept = SlotRules.Drift(keys, before, Keys(inv));
            System.Array.Copy(kept, keys, keys.Length);
        }

        private static string[] Keys(Inventory inv)
        {
            var k = new string[inv.slots.Count];
            for (int i = 0; i < k.Length; i++)
            {
                k[i] = Key(inv.slots[i]);
            }

            return k;
        }

        private static string Json(Inventory inv, int i)
        {
            var it = inv.slots[i].invItem;
            if (InvItemClass.isNull(it))
            {
                return "";
            }

            return JsonConvert.SerializeObject(
                new InvItemClass.SaveState(it, i));
        }

        private static InvItemClass Item(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            var st =
                JsonConvert.DeserializeObject<InvItemClass.SaveState>(json);
            if (st == null)
            {
                return null;
            }

            var it = new InvItemClass();
            st.loadValues(it);
            return it;
        }

        /// <summary>
        /// Make slot i hold this (JSON, "" for empty), and remember it as the
        /// slot's state.
        /// </summary>
        private void Set(Inventory inv, int i, string json)
        {
            if (i < 0 || i >= inv.slots.Count)
            {
                return;
            }

            Fill(inv, i, json);
            if (_watched.TryGetValue(inv, out var keys) && i < keys.Length)
            {
                keys[i] = Key(inv.slots[i]);
            }
        }

        /// <summary>
        /// Make slot i (in range) hold this (JSON, "" for empty). Needs no
        /// session: Sort &amp; Stack played alone uses it too.
        /// </summary>
        private static void Fill(Inventory inv, int i, string json)
        {
            var slot = inv.slots[i];
            slot.inventory = inv;
            // Our player holds an item on the cursor that came from this slot,
            // which someone else has filled now: the game would put the held
            // item back there when it's let go (Controller.placeItem), over
            // theirs. It goes into our bags instead (it's ours: the slot gave
            // it up when we picked it up).
            var ctl = Singleton<Controller>.Instance;
            InvItemClass held = null;
            if (ctl != null)
            {
                held = ctl.pickedUpItem;
            }

            var player = Player.Instance;
            bool holding = !InvItemClass.isNull(held);
            var facts = new HeldFacts
            {
                Filling = !string.IsNullOrEmpty(json),
                Holding = holding,
                FromThisSlot = holding && held.slot == slot,
                PlayerHere = player != null,
            };

            if (SlotRules.HeldGoesToBags(facts))
            {
                var keep = new InvItemClass(held);
                UIInvItem uiInvItem = held.UIInvItem;
                if (uiInvItem != null)
                {
                    uiInvItem.despawn();
                }

                ctl.pickedUpItem = null;
                int dropped = GiveBack(player, keep, keep.amount);
                Plugin.Log.LogInfo(
                    $"[slots] the {keep.type} we held came from slot {i} of "
                        + $"{inv.name}, filled by someone else: into our bags "
                        + $"({dropped} at our feet)");
            }

            Coop.Guards.Replaying = true;
            try
            {
                slot.removeItem();
                var it = Item(json);
                // A gun's saved amount is its loaded rounds (it.ammo); its
                // amount stays the 1 the copy gives it, not 0.
                if (it != null)
                {
                    bool ammo = it.baseClass != null && it.baseClass.hasAmmo;
                    slot.createItem(
                        it, SlotRules.CreateAmount(ammo, it.amount));
                }
            }
            finally
            {
                Coop.Guards.Replaying = false;
            }
        }
    }
}
