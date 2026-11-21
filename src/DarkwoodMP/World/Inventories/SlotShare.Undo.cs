using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.World.Workbenches;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World.Inventories
{
    /// <summary>
    /// SlotShare: undoing a refused change for our player. What was put in
    /// comes back, and what was taken goes again.
    /// </summary>
    internal sealed partial class SlotShare
    {
        /// <summary>
        /// Our change made a slot go from oldKey (its key, "" empty) to tried.
        /// The slot now holds the host's contents (Set); undo the rest for our
        /// player: what we put in comes back into our bags or hotbar (at our
        /// feet only when there's no room), what we took goes again (from the
        /// cursor, the bags, the hotbar; never from the open storage, which is
        /// the host's now).
        /// </summary>
        private static void Undo(
            string oldKey, InvItemClass tried, bool quiet = false)
        {
            var moves = new Moves();
            moves.Add(oldKey, tried);
            Undo(moves, quiet);
        }

        /// <summary>
        /// What slot changes moved, per item type (SlotNet), with the first
        /// item tried of each type, to give back.
        /// </summary>
        private sealed class Moves
        {
            public readonly SlotNet Net = new SlotNet();

            public readonly Dictionary<string, InvItemClass> Items =
                new Dictionary<string, InvItemClass>();

            /// A slot went from oldKey (its key, "" empty) to tried.
            public void Add(string oldKey, InvItemClass tried)
            {
                if (InvItemClass.isNull(tried))
                {
                    Net.Add(oldKey, null, 0);
                    return;
                }

                Net.Add(oldKey, tried.type, tried.amount);
                if (!Items.ContainsKey(tried.type))
                {
                    Items[tried.type] = tried;
                }
            }
        }

        /// <summary>
        /// Undo what net says our changes moved: what we put in comes back to
        /// us, what we took goes again.
        /// </summary>
        private static void Undo(Moves moves, bool quiet)
        {
            var player = Player.Instance;
            if (player == null)
            {
                return;
            }

            bool any = false;
            foreach (var kv in moves.Net.Counts)
            {
                int put = SlotNet.Put(kv.Value);
                int took = SlotNet.Took(kv.Value);

                if (put == 0 && took == 0)
                {
                    continue;
                }

                any = true;
                int dropped = 0;
                if (put > 0
                    && moves.Items.TryGetValue(kv.Key, out var item))
                {
                    dropped = GiveBack(player, item, put);
                }

                int left = took;
                if (left > 0)
                {
                    var ctl = Singleton<Controller>.Instance;
                    InvItemClass held = null;
                    if (ctl != null)
                    {
                        held = ctl.pickedUpItem;
                    }

                    if (!InvItemClass.isNull(held) && held.type == kv.Key)
                    {
                        int n = Mathf.Min(held.amount, left);
                        left -= n;
                        if (n >= held.amount)
                        {
                            UIInvItem uiInvItem = held.UIInvItem;
                            if (uiInvItem != null)
                            {
                                uiInvItem.despawn();
                            }

                            ctl.pickedUpItem = null;
                        }
                        else
                        {
                            held.amount -= n;
                            held.refresh();
                        }
                    }

                    if (left > 0)
                    {
                        left -= player.Inventory.removeItemAmountFromPlayer(
                            kv.Key, left, includeAdditionalInventory: false);
                    }
                }

                string data;
                if (put > 0)
                {
                    data =
                        $"[slots] undone: gave back {put} {kv.Key} ({dropped} "
                            + "of them at our feet, no room)";
                }
                else
                {
                    data =
                        $"[slots] undone: took back {took - left} of {took} "
                            + $"{kv.Key}";
                }

                Plugin.Log.LogInfo(data);
            }

            if (any && !quiet)
            {
                player.displayMessage("Someone took that first");
            }
        }

        /// <summary>
        /// Put amount of this item back where it came from: the player's bags,
        /// then the hotbar (onto a stack of it first, then into a free slot).
        /// Not Inventory.addItemTypeToPlayer: with the workbench open that
        /// counts its storage as the player's, and would put it straight back
        /// there. Returns how many had to be dropped.
        /// </summary>
        private static int GiveBack(
            Player player, InvItemClass item, int amount)
        {
            var bags = new[] { player.Inventory, player.Hotbar };
            int left = amount;
            var baseClass = item.baseClass;
            if (baseClass != null && baseClass.stackable)
            {
                foreach (var bag in bags)
                {
                    foreach (var slot in bag.slots)
                    {
                        if (left <= 0)
                        {
                            break;
                        }

                        var it = slot.invItem;
                        if (InvItemClass.isNull(it) || it.type != item.type
                            || it.amount >= it.baseClass.maxAmount)
                        {
                            continue;
                        }

                        int n =
                            Mathf.Min(left, it.baseClass.maxAmount - it.amount);
                        var moved = new InvItemClass(item) { amount = n };
                        it.durability =
                            InvItemClass.getStackedDurability(it, moved);
                        it.amount += n;
                        it.refresh();
                        left -= n;
                    }
                }
            }

            foreach (var bag in bags)
            {
                if (left <= 0)
                {
                    break;
                }

                foreach (var slot in bag.slots)
                {
                    if (!InvItemClass.isNull(slot.invItem))
                    {
                        continue;
                    }

                    if (slot.inventory == null)
                    {
                        slot.inventory = bag;
                    }

                    slot.createItem(item, left);
                    left = 0;
                    break;
                }
            }

            if (left > 0)
            {
                player.spawnDroppedInvItem(new InvItemClass(item)
                {
                    amount = left
                });
            }

            return left;
        }
    }
}
