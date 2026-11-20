namespace DarkwoodMP.World.Inventories
{
    /// How slots are compared between games (the plugin's SlotShare.Key,
    /// Drifts and RotOnly). A slot's key is
    /// "type|amount|durability x 1000|recipe 0/1|upgrades", "" when empty.
    public static class SlotKeyRules
    {
        /// How far two games' durabilities of one drifting item may be
        /// apart and still count as the same item, as a share of its
        /// maximum.
        public const float DriftTolerance = 0.25f;

        /// The key of a slot: its item's type, amount, durability in
        /// thousandths (rounded half to even, as Mathf.RoundToInt does, on
        /// the float product), recipe and number of upgrades.
        public static string Of(in SlotItem item)
        {
            if (!item.Filled)
            {
                return "";
            }

            float milli = item.Durability * 1000f;
            int rounded = (int)System.Math.Round(milli);
            string recipe = "0";
            if (item.Recipe)
            {
                recipe = "1";
            }

            return item.Type + "|" + item.Amount + "|" + rounded + "|"
                + recipe + "|" + item.Upgrades;
        }

        /// Whether the world changes the item's durability by itself in a
        /// container: food that rots, and items that recharge while off or
        /// drain while on.
        public static bool Drifts(in SlotItem item)
        {
            if (!item.Filled || !item.HasBase)
            {
                return false;
            }

            return item.Rots || item.Regenerates || item.Drain > 0f;
        }

        /// Whether keys a and b name the same item but for its durability,
        /// by no more than DriftTolerance of its maximum (at least 1), when
        /// the item here drifts. Equal keys are always the same, for any
        /// item. A key that isn't five parts, or whose durability isn't a
        /// number, never matches another.
        public static bool RotOnly(in SlotItem here, string a, string b)
        {
            if (a == b)
            {
                return true;
            }

            if (!Drifts(here) || string.IsNullOrEmpty(a))
            {
                return false;
            }

            if (string.IsNullOrEmpty(b))
            {
                return false;
            }

            var x = a.Split('|');
            var y = b.Split('|');
            if (x.Length != 5 || y.Length != 5)
            {
                return false;
            }

            if (x[0] != y[0] || x[1] != y[1] || x[3] != y[3] || x[4] != y[4])
            {
                return false;
            }

            if (!int.TryParse(x[2], out int dx))
            {
                return false;
            }

            if (!int.TryParse(y[2], out int dy))
            {
                return false;
            }

            float max = Max(1f, here.MaxDurability) * 1000f;
            return System.Math.Abs(dx - dy) <= DriftTolerance * max;
        }

        /// As Mathf.Max: b unless a is larger.
        static float Max(float a, float b)
        {
            if (a > b)
            {
                return a;
            }

            return b;
        }
    }
}
