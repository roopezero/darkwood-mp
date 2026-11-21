namespace DarkwoodMP.World.Breaking
{
    /// What a game knows after an Item.die or Item.setNormalState.
    public readonly struct ItemChangeFacts
    {
        /// It was the outermost patched call (WorldActions.Outermost).
        public bool Outermost { get; init; }

        /// WorldActions.Sharing.
        public bool Sharing { get; init; }

        /// The item is still there (not null).
        public bool Exists { get; init; }

        /// Item.destroyed before the call.
        public bool WasDestroyed { get; init; }

        /// Item.destroyed after it.
        public bool Destroyed { get; init; }
    }
}
