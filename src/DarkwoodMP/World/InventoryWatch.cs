using System;
using System.Collections.Generic;
using DarkwoodMP.Combat;

namespace DarkwoodMP.World
{
    /// <summary>
    /// What the local player carries, by item type, as it was at the end of the
    /// last frame: what a request's frame added or used up is the difference,
    /// which a refused request gives back or takes away again.
    /// </summary>
    internal sealed class InventoryWatch
    {
        private Dictionary<string, int> _base;

        /// <summary>
        /// Every frame (late): remember what the player carries now.
        /// </summary>
        public void LateUpdate()
        {
            if (!NetEnemies.ClientMode || !WorldShare.InWorld)
            {
                _base = null;
                return;
            }

            _base = Count();
        }

        /// <summary>
        /// What changed since the frame began (and start counting from here,
        /// for the next request).
        /// </summary>
        public Dictionary<string, int> TakeChange()
        {
            var now = Count();
            var change = InventoryRules.Change(_base, now);
            _base = now;
            return change;
        }

        /// <summary>
        /// Give back what was used up, take away what was gained.
        /// </summary>
        public void Undo(Dictionary<string, int> change)
        {
            var p = Player.Instance;
            if (change == null || p == null)
            {
                return;
            }

            foreach (var kv in change)
            {
                try
                {
                    if (kv.Value < 0)
                    {
                        p.Inventory.addItemTypeToPlayer(
                            kv.Key, -kv.Value, dropIfNoRoom: true);
                    }
                    else if (kv.Value > 0)
                    {
                        p.Inventory.removeItemAmountFromPlayer(
                            kv.Key, kv.Value);
                    }

                    string text = "took back";
                    if (kv.Value < 0)
                    {
                        text = "gave back";
                    }

                    Plugin.Log.LogInfo(
                        $"[authority] {text} {Math.Abs(kv.Value)} {kv.Key}");
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"[authority] couldn't undo {kv.Value} {kv.Key}: "
                            + $"{e.Message}");
                }
            }

            _base = Count();
        }

        private Dictionary<string, int> Count()
        {
            var p = Player.Instance;
            if (p == null || p.Inventory == null)
            {
                return null;
            }

            var d = new Dictionary<string, int>();
            void Add(Inventory inv)
            {
                if (inv == null || inv.slots == null)
                {
                    return;
                }

                foreach (var slot in inv.slots)
                {
                    InvItemClass it = null;
                    if (slot != null)
                    {
                        it = slot.invItem;
                    }

                    if (InvItemClass.isNull(it))
                    {
                        continue;
                    }

                    d.TryGetValue(it.type, out int n);
                    d[it.type] = n + InventoryRules.CountOf(it.amount);
                }
            }

            Add(p.Inventory);
            Add(p.Hotbar);
            return d;
        }
    }
}
