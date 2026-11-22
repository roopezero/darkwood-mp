namespace DarkwoodMP.World.Inventories
{
    /// The workbench's level, shared by everyone (the plugin's
    /// SlotShare.OnLevel): the highest wins.
    public static class WorkbenchLevelRules
    {
        /// Whether a level from another game is above ours. One that isn't
        /// is ignored, with nothing logged.
        public static bool Raises(int level, int current)
        {
            return level > current;
        }

        /// Host: whether a client's raised level stands. An upgrade
        /// (CraftingRecipes.doCraft) raises it by one, at a workbench. The
        /// level is checked first. A refusal is only logged: the client is
        /// sent nothing, and its level stays raised.
        public static Verdict MayRaise(in LevelFacts raise)
        {
            if (raise.Level != raise.Current + 1)
            {
                return Verdict.Refuse(
                    Refusal.LevelNotNext,
                    Refusals.Detail(
                        raise.Level.ToString(),
                        raise.Current.ToString()));
            }

            if (!raise.AtWorkbench)
            {
                return Verdict.Refuse(Refusal.NotAtWorkbench);
            }

            return Verdict.Allow;
        }
    }
}
