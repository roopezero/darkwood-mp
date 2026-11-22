namespace DarkwoodMP.World.Inventories
{
    /// What Containers.Poll looks at in a container the local player has
    /// open.
    public readonly struct OpenContainerFacts
    {
        /// Inventory.invType.
        public ContainerKind Kind { get; init; }

        /// Inventory.isWorkbench: the workbench's own inventory, its
        /// recipes, not its storage.
        public bool Workbench { get; init; }

        /// Only our player has it, in a dream of their own
        /// (WorldActions.OursAlone).
        public bool OursAlone { get; init; }

        /// It's a workbench's storage (SlotShare.Is).
        public bool Storage { get; init; }

        /// This game is a client of the session.
        public bool IsClient { get; init; }
    }
}
