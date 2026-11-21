namespace DarkwoodMP.World.Hideout
{
    /// A GameEvent step, as the world-move check sees it.
    public readonly struct WorldMoveFacts
    {
        /// Its type is moveInventory.
        public bool MovesInventory { get; init; }

        /// It has a sourceTransform.
        public bool HasSource { get; init; }

        /// getTargetGameObjectsFromParent.
        public bool FromParent { get; init; }

        /// getTargetFromLocation.
        public bool FromLocation { get; init; }

        /// locationObjects.Count (0 when null).
        public int LocationObjects { get; init; }

        /// The source has an Inventory.
        public bool SourceInventory { get; init; }

        /// That Inventory is a player's (WorldMoveRules.IsPlayers).
        public bool SourcePlayers { get; init; }

        /// The targets (targetGameObjects, then targetUniqueObjects found)
        /// that have an Inventory.
        public int TargetInventories { get; init; }

        /// One of those is a player's.
        public bool TargetPlayers { get; init; }
    }
}
