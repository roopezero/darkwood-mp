namespace DarkwoodMP.World.Workbenches
{
    /// How many slots a workbench's storage has (the plugin's
    /// WorkbenchStorage): its prefab's count, plus one row for each
    /// workbench level above the first. It only ever grows. These run
    /// alone too, with no session.
    public static class WorkbenchRules
    {
        /// Slots added for each level above the first: one row of the
        /// storage's six columns.
        public const int RowSize = 6;

        /// The level a new game starts at (Controller.workbenchLevel's
        /// default), and the level when there is no Controller.
        public const int BaseLevel = 1;

        /// The storage's slot count at the base level when its prefab
        /// can't be found.
        public const int FallbackBase = 30;

        /// From this many slots on, a storage is drawn RowSize columns
        /// wide: the game's rule for big containers (Inventory.init).
        public const int WideFrom = 30;

        /// The slot count the storage should have at this level. A level
        /// below the base adds nothing.
        public static int Wanted(int baseSlots, int level)
        {
            int above = level - BaseLevel;
            if (above < 0)
            {
                above = 0;
            }

            return baseSlots + RowSize * above;
        }

        /// Whether a storage of this many slots grows to the wanted count.
        /// It never shrinks: one with more keeps them.
        public static bool Grows(int slots, int wanted)
        {
            return slots < wanted;
        }

        /// Whether a storage that grew to this many slots is given RowSize
        /// columns (maxColumns). A smaller one keeps its own.
        public static bool Wide(int slots)
        {
            return slots >= WideFrom;
        }

        /// Every frame (WorkbenchStorage.Update): whether every workbench
        /// is given its rows now, and why. Not while no world is ready,
        /// which forgets the scene, so the next ready world counts as
        /// loaded. Then once for each new scene ("world loaded") and each
        /// change of level in it ("level changed").
        public static RowsUpdate Update(RowsApplied applied, RowsFacts now)
        {
            if (!now.Ready)
            {
                var forgot = applied with { Scene = RowsApplied.NoScene };
                return new RowsUpdate { Applied = forgot };
            }

            if (now.Scene == applied.Scene && now.Level == applied.Level)
            {
                return new RowsUpdate { Applied = applied };
            }

            var why = RowsWhy.LevelChanged;
            if (now.Scene != applied.Scene)
            {
                why = RowsWhy.WorldLoaded;
            }

            var next = new RowsApplied
            {
                Scene = now.Scene,
                Level = now.Level,
            };

            return new RowsUpdate { Applied = next, Why = why };
        }
    }
}
