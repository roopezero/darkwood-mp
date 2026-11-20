using System.Collections.Generic;

namespace DarkwoodMP.World
{
    /// What a client's request frame added to or used up from its player's
    /// inventory (the plugin's InventoryWatch), so a refused request can
    /// give it back or take it away again.
    public static class InventoryRules
    {
        /// The least one slot adds to its item type's count.
        public const int LeastCount = 1;

        /// What one slot adds to its item type's count: an item with an
        /// amount of 0 counts as 1 (LeastCount).
        public static int CountOf(int amount)
        {
            if (amount > LeastCount)
            {
                return amount;
            }

            return LeastCount;
        }

        /// For each item type whose count differs, now minus before; for
        /// each type gone, minus before. Null when nothing changed, or when
        /// either count is missing (no player, or none taken yet).
        public static Dictionary<string, int> Change(
            Dictionary<string, int> before,
            Dictionary<string, int> now)
        {
            if (now == null || before == null)
            {
                return null;
            }

            var change = new Dictionary<string, int>();
            foreach (var kv in now)
            {
                before.TryGetValue(kv.Key, out int was);
                if (kv.Value != was)
                {
                    change[kv.Key] = kv.Value - was;
                }
            }

            foreach (var kv in before)
            {
                if (!now.ContainsKey(kv.Key))
                {
                    change[kv.Key] = -kv.Value;
                }
            }

            if (change.Count > 0)
            {
                return change;
            }

            return null;
        }
    }
}
