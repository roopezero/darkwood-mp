namespace DarkwoodMP.World.Stacks
{
    /// One stack size for every item that stacks by amount (the plugin's
    /// StackSizes). A size only raises a stack, and 0 means the game's own
    /// sizes. In a session a client uses the host's size.
    public static class StackRules
    {
        /// The host's size of a game that isn't a client in a session.
        public const int NoHostSize = -1;

        /// An item that stacks to this or less doesn't stack by amount.
        public const int Single = 1;

        /// The most a StackSize message holds (u16).
        public const int MostOnWire = ushort.MaxValue;

        /// Whether an item stacks by amount: it stacks, its amount is
        /// neither fuel nor rounds, and it stacks to more than one.
        public static bool Eligible(in StackItemFacts item)
        {
            return item.Stackable
                && !item.StacksFuel
                && !item.HoldsAmmo
                && item.Max > Single;
        }

        /// An eligible item's stack size with size in use: the bigger of
        /// the game's own and size, or the game's own when size is 0 or
        /// less.
        public static int SizeFor(int original, int size)
        {
            if (size > 0)
            {
                return Max(original, size);
            }

            return original;
        }

        /// Whether an item may stand in one slot (SlotShare): no more than
        /// its stack size now. Always, with no item or one that doesn't
        /// stack by amount.
        public static bool Fits(in StackSlotFacts slot)
        {
            if (!slot.Exists)
            {
                return true;
            }

            if (!Eligible(slot.Item))
            {
                return true;
            }

            return slot.Amount <= slot.Item.Max;
        }

        /// The size in use: the host's (hostSize, NoHostSize for none),
        /// else our own setting, never less than 0.
        public static int Current(int hostSize, int setting)
        {
            if (hostSize >= 0)
            {
                return hostSize;
            }

            return Max(0, setting);
        }

        /// Whether we are a client using the host's size. The session's
        /// end puts our own setting back then, and only then.
        public static bool FollowsHost(int hostSize)
        {
            return hostSize >= 0;
        }

        /// What a change of our own setting does.
        public static StackChange OnSettingChanged(int hostSize, bool isHost)
        {
            if (FollowsHost(hostSize))
            {
                return StackChange.Ignore;
            }

            if (isHost)
            {
                return StackChange.ApplyAndSend;
            }

            return StackChange.Apply;
        }

        /// Whether a StackSize message is taken: by a client, from the
        /// host.
        public static bool TakesSize(bool isClient, bool fromHost)
        {
            return isClient && fromHost;
        }

        /// The size as a StackSize message holds it: clamped to 0 to
        /// MostOnWire.
        public static ushort OnWire(int size)
        {
            if (size < 0)
            {
                return 0;
            }

            if (size > MostOnWire)
            {
                return MostOnWire;
            }

            return (ushort)size;
        }

        /// As Mathf.Max: a when it is bigger, else b.
        static int Max(int a, int b)
        {
            if (a > b)
            {
                return a;
            }

            return b;
        }
    }
}
