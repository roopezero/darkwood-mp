using System.Collections.Generic;

namespace DarkwoodMP.World.Inventories
{
    /// What the host looks at in a batch of slot changes a client made in
    /// one look at a container (SlotShare.OnSlots).
    public readonly struct SlotBatchFacts
    {
        /// ContainerRules.MayChange for the client and the container.
        public Verdict Container { get; init; }

        /// It's a workbench's storage, which several players use at once.
        public bool Storage { get; init; }

        /// Another player holds its lock (ContainerLocks.HeldByOther).
        public bool HeldByOther { get; init; }

        /// That player's name (Session.NameOf), when HeldByOther.
        public string Holder { get; init; }

        /// How many slots the container has in the host's world.
        public int SlotCount { get; init; }

        /// The changes, in the order sent.
        public IReadOnlyList<SlotChangeFacts> Changes { get; init; }
    }
}
