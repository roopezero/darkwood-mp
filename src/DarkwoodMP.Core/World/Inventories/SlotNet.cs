using System.Collections.Generic;

namespace DarkwoodMP.World.Inventories
{
    /// What a client's slot changes moved, per item type, to undo them
    /// (the plugin's SlotShare.Moved and the net it fills): more of a type
    /// in the container (we put it in) counts up, less (we took it) counts
    /// down. Over a whole batch, an item moved from one slot of the
    /// container to another comes to nothing. The shell keeps the first
    /// item tried of each type, to give back.
    public sealed class SlotNet
    {
        readonly Dictionary<string, int> _counts =
            new Dictionary<string, int>();

        /// The net count of each item type, in the order first added.
        public IReadOnlyDictionary<string, int> Counts => _counts;

        /// A slot went from the item named by oldKey (its slot key, ""
        /// when empty) to tried (its type and amount; a null type when the
        /// slot was made empty). An old key with no amount counts 0.
        public void Add(string oldKey, string tried, int amount)
        {
            if (tried != null)
            {
                Count(tried, amount);
            }

            if (oldKey.Length == 0)
            {
                return;
            }

            var old = oldKey.Split('|');
            int had = 0;
            if (old.Length > 1 && int.TryParse(old[1], out int parsed))
            {
                had = parsed;
            }

            Count(old[0], -had);
        }

        /// How many of a net count we put in: to give back.
        public static int Put(int net)
        {
            if (net > 0)
            {
                return net;
            }

            return 0;
        }

        /// How many of a net count we took: to take back.
        public static int Took(int net)
        {
            if (net < 0)
            {
                return -net;
            }

            return 0;
        }

        void Count(string type, int amount)
        {
            _counts.TryGetValue(type, out int was);
            _counts[type] = was + amount;
        }
    }
}
