namespace DarkwoodMP.World.Progress
{
    /// The stove's level without the game's skill choice
    /// (Progression.TakeLevelAtStove, StoveLevelPatch) and the overlay that
    /// shows what's waiting there (Progression.OnGUI).
    public static class StoveRules
    {
        /// What a level gives the player's saturation (gainSaturation).
        public const float Saturation = 5f;

        /// The level whose dream is set by name.
        public const int FirstDreamLevel = 2;

        /// The dream the first level dream goes into.
        public const string FirstDream = "dream_bunker_underground_01";

        /// The levels that bring a dream, once each (Dreams.hadDreamAtLvlN).
        static readonly int[] DreamLevels = { 2, 3, 5, 6, 7 };

        /// Whether the game's showHideTiers runs as it is: only to show the
        /// tiers, or with the game's skill choice open. Otherwise the mod
        /// gives the level.
        public static bool RunsGame(bool onlyShow, bool opened)
        {
            return onlyShow || opened;
        }

        /// Whether the new level brings its dream: one of 2, 3, 5, 6 and 7,
        /// its dream not had yet, and dreams not disabled. The shell then
        /// sets wantToDream and the level's flag, and for level 2 the
        /// dream's name.
        public static bool BringsDream(int level, bool had, bool disabled)
        {
            if (disabled || had)
            {
                return false;
            }

            foreach (var each in DreamLevels)
            {
                if (each == level)
                {
                    return true;
                }
            }

            return false;
        }

        /// Whether the dream's transition starts: wanted, and either the
        /// world isn't random or a preset is left for the chapter. The
        /// shell asks the game for the preset only when it is random.
        /// Otherwise the shell sets wantToDream back to false.
        public static bool Transitions(
            bool wanted,
            bool random,
            bool presetLeft)
        {
            return wanted && (!random || presetLeft);
        }

        /// Whether the stove's overlay may show: in a world, no mod menu
        /// over it, and the player can reset here (CanResetHere).
        public static bool OverlayOpen(bool inWorld, bool menuOver, bool here)
        {
            return inWorld && !menuOver && here;
        }

        /// The shared levels our player can still take at a stove: only
        /// when the progression is shared (Hideouts.Together, or alone).
        public static int LevelsToTake(bool sharing, int shared, int own)
        {
            if (!sharing)
            {
                return 0;
            }

            int left = shared - own;
            if (left > 0)
            {
                return left;
            }

            return 0;
        }

        /// Whether the overlay draws: a level to take, a point to spend,
        /// or the morning's reset button.
        public static bool OverlayShows(int levels, int points, bool morning)
        {
            return levels > 0 || points > 0 || morning;
        }
    }
}
