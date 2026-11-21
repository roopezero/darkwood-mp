namespace DarkwoodMP.World.Progress
{
    /// The shared essence and level (Progression): what a player sees, how
    /// many levels the essence allows, and the host's checks of a client's
    /// cooking and level.
    public static class ProgressRules
    {
        /// Past this shared level the bar counts as full, whatever the
        /// essence (BarFull's literal 7).
        public const int LastBarLevel = 7;

        /// The least essence one cooking may add.
        public const int LeastEssence = 1;

        /// The most essence one cooking may add.
        public const int MostEssence = 100000;

        /// The essence a level needs: the level clamped into the list, so a
        /// level past the end reads the last one; 0 with no list.
        public static int Requirement(int[] requirements, int level)
        {
            if (requirements == null || requirements.Length == 0)
            {
                return 0;
            }

            int last = requirements.Length - 1;
            if (level < 0)
            {
                return requirements[0];
            }

            if (level > last)
            {
                return requirements[last];
            }

            return requirements[level];
        }

        /// The essence a player with this own level sees: the shared
        /// essence, up to their next level.
        public static int Display(in ProgressFacts progress, int own)
        {
            int need = Requirement(progress.Requirements, own);
            if (progress.SharedExp < need)
            {
                return progress.SharedExp;
            }

            return need;
        }

        /// The most levels the shared essence allows: level L needs the
        /// requirement of L - 1.
        public static int Allowed(in ProgressFacts progress)
        {
            int most = 0;
            if (progress.Requirements != null)
            {
                most = progress.Requirements.Length;
            }

            int level = 0;
            while (level < most
                && progress.SharedExp
                    >= Requirement(progress.Requirements, level))
            {
                level++;
            }

            return level;
        }

        /// The most levels a player can have now: the shared level, or what
        /// the shared essence allows. With no player, the shared level.
        public static int MostLevels(in ProgressFacts progress)
        {
            if (!progress.HasPlayer)
            {
                return progress.SharedLevel;
            }

            int allowed = Allowed(progress);
            if (progress.SharedLevel > allowed)
            {
                return progress.SharedLevel;
            }

            return allowed;
        }

        /// Whether the shared bar is full: someone has to take the level at
        /// a stove before more essence counts.
        public static bool BarFull(in ProgressFacts progress)
        {
            return progress.SharedLevel > LastBarLevel
                || progress.SharedExp
                    >= Requirement(
                        progress.Requirements,
                        progress.SharedLevel);
        }

        /// The host's own game merged into the shared numbers
        /// (Progression.HostSync): the larger essence and the larger level.
        /// When the result differs from shared, the shell stores, logs and
        /// sends it.
        public static ProgressNumbers Merge(
            ProgressNumbers shared,
            ProgressNumbers own)
        {
            int exp = shared.Exp;
            if (own.Exp > exp)
            {
                exp = own.Exp;
            }

            int level = shared.Level;
            if (own.Level > level)
            {
                level = own.Level;
            }

            return new ProgressNumbers { Exp = exp, Level = level };
        }

        /// Whether the host takes in a client's EssenceAdd or LevelTaken:
        /// only as the host, with the shared record and a player. Otherwise
        /// the handler returns without an answer. The shell reads the
        /// record (which can start it) only when isHost, as today.
        public static bool Handles(bool isHost, bool hasRecord, bool hasPlayer)
        {
            return isHost && hasRecord && hasPlayer;
        }

        /// The host's check of a client's cooking (Progression.OnEssence),
        /// after Handles. The shell sends the requester the shared state
        /// before it refuses. Allowed: the shared essence grows by the
        /// amount, not capped at the bar.
        public static Verdict MayAddEssence(
            in ProgressFacts progress,
            int amount)
        {
            if (amount < LeastEssence || amount > MostEssence)
            {
                return Verdict.Refuse(
                    Refusal.ImplausibleEssence,
                    amount.ToString());
            }

            if (BarFull(progress))
            {
                return Verdict.Refuse(Refusal.EssenceFull);
            }

            return Verdict.Allow;
        }

        /// The level the host takes from a client's LevelTaken: what it
        /// says, capped at what the shared essence allows. The shell logs
        /// when the report was more.
        public static int AcceptedLevel(in ProgressFacts progress, int reported)
        {
            int allowed = Allowed(progress);
            if (reported < allowed)
            {
                return reported;
            }

            return allowed;
        }

        /// Whether an accepted level raises the shared level.
        public static bool Raises(in ProgressFacts progress, int accepted)
        {
            return accepted > progress.SharedLevel;
        }

        /// Whether the game's consumeExp counts as our player's cooking
        /// (ConsumeExpPatch): a player, not a replay, and the essence grew.
        public static bool Cooked(bool hasPlayer, bool replaying, int added)
        {
            return hasPlayer && !replaying && added > 0;
        }
    }
}
