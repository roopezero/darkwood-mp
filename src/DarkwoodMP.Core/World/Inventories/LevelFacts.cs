namespace DarkwoodMP.World.Inventories
{
    /// What the host looks at when a client raised the workbench's level
    /// (SlotShare.OnLevel).
    public readonly struct LevelFacts
    {
        /// The level the client sent.
        public int Level { get; init; }

        /// The host's level now (Controller.workbenchLevel).
        public int Current { get; init; }

        /// The client's stand-in is within ReachRules.Reach of a
        /// workbench, active or not (Authority.Near for each; true when it
        /// has no stand-in).
        public bool AtWorkbench { get; init; }
    }
}
