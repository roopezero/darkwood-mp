namespace DarkwoodMP.World.Dive
{
    /// The village cellar's dive, shared (the plugin's SharedTank). Once
    /// any player holds a full oxygen tank, a flag of the mod's own says
    /// so, and the dive's requirement passes for every player. This runs
    /// in a game played alone too: the flag is kept for when others join.
    public static class SharedTankRules
    {
        /// The full tank's item type.
        public const string Item = "oxygenTank_full";

        /// The mod's flag (FlagsSync.ModPrefix + "oxygenTankFilled").
        public const string Flag = "dwmp_oxygenTankFilled";

        /// How often, in seconds of unscaled time, the watcher looks.
        public const float Interval = 2f;

        /// Whether the watcher looks now.
        public static bool Due(Seconds now, Seconds next)
        {
            return now.Value >= next.Value;
        }

        /// When the watcher looks next. It is set whenever it is due, even
        /// when it then looks no further.
        public static Seconds Next(Seconds now)
        {
            return new Seconds(now.Value + Interval);
        }

        /// Whether the watcher looks at the player: in a world, not loading
        /// or in the main menu, the flag not set yet, and a player with an
        /// inventory.
        public static bool Looks(in WatchFacts world)
        {
            if (!world.InWorld || world.Loading || world.MainMenu)
            {
                return false;
            }

            return !world.Filled && world.Player;
        }

        /// Whether to set the flag: the player holds a full tank, and the
        /// game's Flags are there. It is never cleared.
        public static bool Sets(in HoldFacts player)
        {
            if (!player.InBags && !player.InJournal)
            {
                return false;
            }

            return player.Flags;
        }

        /// Whether the flag reads as set: in Flags.flagsDict, not null, and
        /// true.
        public static bool Filled(bool found, bool isTrue)
        {
            return found && isTrue;
        }

        /// Whether an item type is the full tank (InvItem.type).
        public static bool IsTank(string type)
        {
            return type == Item;
        }

        /// The requirement's result: once the flag is set, "has a full
        /// tank" passes for every player and its "no tank" counterpart
        /// fails. Any other requirement keeps the game's result.
        public static bool Requirement(in RequirementFacts need)
        {
            if (!need.HaveItem || !need.Tank || !need.Filled)
            {
                return need.Result;
            }

            return need.ActiveModifier;
        }
    }
}
