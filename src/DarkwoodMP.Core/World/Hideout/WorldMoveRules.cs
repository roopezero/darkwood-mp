namespace DarkwoodMP.World.Hideout
{
    /// A scripted moveInventory step between world containers (the
    /// plugin's Hideouts.IsWorldMove): world state, so the host runs it
    /// and sends what each container holds afterwards.
    public static class WorldMoveRules
    {
        /// A moveInventory step with a source, no targets found at run
        /// time, no player's inventory in it, and at least one target
        /// inventory.
        public static bool IsWorldMove(in WorldMoveFacts step)
        {
            if (!step.MovesInventory || !step.HasSource)
            {
                return false;
            }

            if (step.FromParent || step.FromLocation)
            {
                return false;
            }

            if (step.LocationObjects > 0)
            {
                return false;
            }

            if (!step.SourceInventory || step.SourcePlayers)
            {
                return false;
            }

            return !step.TargetPlayers && step.TargetInventories > 0;
        }

        /// Whether an inventory is a player's: under a Player, or the
        /// local player's Inventory or Hotbar.
        public static bool IsPlayers(bool under, bool bag, bool hotbar)
        {
            return under || bag || hotbar;
        }

        /// Whether the host sends the containers after the step ran.
        public static bool SendsAfter(bool together, bool host, bool store)
        {
            return together && host && store;
        }
    }
}
