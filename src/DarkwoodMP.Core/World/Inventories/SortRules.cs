using System.Collections.Generic;

namespace DarkwoodMP.World.Inventories
{
    /// Sort & Stack for a workbench's storage (the plugin's SlotShare.Sort,
    /// OnSort and SortPressed). Stackable items of one kind merge into the
    /// earlier stacks, as the game merges them; fuel kept as durability
    /// pours into the earlier ones of its type; then the stacks go in order
    /// of category, type, amount (most first), durability (most first) and
    /// old slot.
    public static class SortRules
    {
        /// The category of an item that has none: after all the others.
        public const int NoCategory = 10000;

        /// Fuel at or under this counts as none: a can poured empty.
        public const float NoFuel = 0.001f;

        /// What pressing Sort & Stack does. A client sorts only with the
        /// host's contents as its starting point, and asks the host.
        public static SortPress Pressed(in SortPressFacts press)
        {
            if (press.Holding)
            {
                return SortPress.Nothing;
            }

            if (!press.IsClient || !press.InWorld)
            {
                return SortPress.Sort;
            }

            if (!press.Named)
            {
                return SortPress.NoName;
            }

            if (press.Waiting)
            {
                return SortPress.StillWaiting;
            }

            return SortPress.PredictAndAsk;
        }

        /// Whether the host sends the slots its own sort changed: only the
        /// host, when some changed, for a storage with a name.
        public static bool SendsSorted(bool isHost, int changed, bool named)
        {
            return isHost && changed > 0 && named;
        }

        /// Host: whether a client's Sort & Stack is done
        /// (SlotShare.OnSort). The first of: no such storage here; the
        /// container's own reasons (too far, locked); the host's player is
        /// moving an item from it.
        public static Verdict MaySort(in SortFacts sort)
        {
            if (!sort.StorageHere)
            {
                return Verdict.Refuse(Refusal.NoSuchStorage);
            }

            if (sort.Container.Refused)
            {
                return sort.Container;
            }

            if (sort.HostMoving)
            {
                return Verdict.Refuse(Refusal.HostMovingItem);
            }

            return Verdict.Allow;
        }

        /// The storage's stacks, in slot order, merged, poured and sorted.
        /// Stacks merged or poured away are gone from the list, and each
        /// stack's place in it is its new slot.
        public static List<SortedStack> Sort(IReadOnlyList<SortStack> stacks)
        {
            var work = new List<Work>(stacks.Count);
            foreach (var stack in stacks)
            {
                work.Add(new Work(stack));
            }

            MergeAll(work);
            PourAll(work);
            work.RemoveAll(w => (w.Amount <= 0 && w.Mergeable) || w.Empty);
            work.Sort(Compare);
            var sorted = new List<SortedStack>(work.Count);
            foreach (var w in work)
            {
                sorted.Add(new SortedStack
                {
                    From = w.Item.Slot,
                    Amount = w.Amount,
                    Durability = w.Durability,
                });
            }

            return sorted;
        }

        /// Each mergeable stack tops up the earlier stacks of the same
        /// item, in slot order, up to their most; the topped-up stack's
        /// durability is the average weighted by amount.
        static void MergeAll(List<Work> work)
        {
            for (int b = 0; b < work.Count; b++)
            {
                var src = work[b];
                if (!src.Mergeable)
                {
                    continue;
                }

                for (int a = 0; a < b && src.Amount > 0; a++)
                {
                    var dst = work[a];
                    if (!dst.Mergeable || !dst.SameItem(src))
                    {
                        continue;
                    }

                    int most = dst.Item.MaxAmount;
                    if (dst.Amount <= 0 || dst.Amount >= most)
                    {
                        continue;
                    }

                    int n = Min(src.Amount, most - dst.Amount);
                    float total = dst.Amount * dst.Durability
                        + src.Durability * n;
                    dst.Durability = total / (dst.Amount + n);
                    dst.Amount += n;
                    src.Amount -= n;
                }
            }
        }

        /// Each item whose fuel is its durability tops up the earlier ones
        /// of its type, in slot order, up to their maximum.
        static void PourAll(List<Work> work)
        {
            for (int b = 0; b < work.Count; b++)
            {
                var src = work[b];
                if (!src.Pours)
                {
                    continue;
                }

                for (int a = 0; a < b && src.Durability > NoFuel; a++)
                {
                    var dst = work[a];
                    float max = dst.Item.MaxDurability;
                    if (!dst.Pours || dst.Item.Type != src.Item.Type)
                    {
                        continue;
                    }

                    if (dst.Durability >= max)
                    {
                        continue;
                    }

                    float n = Min(src.Durability, max - dst.Durability);
                    dst.Durability += n;
                    src.Durability -= n;
                }
            }
        }

        /// Category, then type (a recipe as "~" and what it makes, by
        /// ordinal), then amount and durability, most first, then old slot.
        static int Compare(Work x, Work y)
        {
            int c = x.Item.Category.CompareTo(y.Item.Category);
            if (c == 0)
            {
                c = string.CompareOrdinal(x.Name, y.Name);
            }

            if (c == 0)
            {
                c = y.Amount.CompareTo(x.Amount);
            }

            if (c == 0)
            {
                c = y.Durability.CompareTo(x.Durability);
            }

            if (c == 0)
            {
                c = x.Item.Slot.CompareTo(y.Item.Slot);
            }

            return c;
        }

        /// As Mathf.Min: b unless a is smaller.
        static int Min(int a, int b)
        {
            if (a < b)
            {
                return a;
            }

            return b;
        }

        /// As Mathf.Min: b unless a is smaller.
        static float Min(float a, float b)
        {
            if (a < b)
            {
                return a;
            }

            return b;
        }

        /// A stack while it's merged and poured.
        sealed class Work
        {
            public readonly SortStack Item;
            public int Amount;
            public float Durability;

            public Work(SortStack item)
            {
                Item = item;
                Amount = item.Amount;
                Durability = item.Durability;
            }

            bool Plain => Item.Upgrades == 0 && Item.Modifiers == 0;

            /// Stacks with others of its kind: a stackable item with no
            /// rounds, upgrades or modifiers, more than one to a stack.
            public bool Mergeable =>
                Item.HasBase && Item.Stackable && !Item.HasAmmo
                && Item.MaxAmount > 1 && Plain;

            /// Its fuel is its durability: poured, never stacked.
            public bool Pours =>
                Item.HasBase && Item.StacksDurability
                && Item.MaxDurability > 0f && Plain;

            /// The game takes it away when it's empty
            /// (InvItemClass.drainDurability).
            bool GoneWhenEmpty =>
                !Item.HasAmmo && !Item.Reloadable && !Item.Repairable
                && !Item.Regenerates && !Item.Rots;

            /// Poured empty, and gone as the game would take it.
            public bool Empty => Pours && GoneWhenEmpty && Durability <= NoFuel;

            /// What it's sorted by within a category.
            public string Name
            {
                get
                {
                    if (Item.Recipe)
                    {
                        return "~" + Item.RecipeFor;
                    }

                    return Item.Type;
                }
            }

            public bool SameItem(Work other)
            {
                if (Item.Type != other.Item.Type)
                {
                    return false;
                }

                return Item.Recipe == other.Item.Recipe
                    && Item.RecipeFor == other.Item.RecipeFor;
            }
        }
    }
}
