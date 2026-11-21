namespace DarkwoodMP.World.Locks
{
    /// What a game knows when one of its Items may have just become
    /// searched: Item.disarm (the plugin's CrateSearchedPatch) or
    /// Item.openInventory (OpenSearchedPatch) returned.
    public readonly struct SearchSentFacts
    {
        /// Item.searched before the call.
        public bool WasSearched { get; init; }

        /// Item.searched after it.
        public bool Searched { get; init; }

        /// It is a crate picked open with the progress bar
        /// (LockRules.IsCrate). Only for the disarm.
        public bool Crate { get; init; }

        /// WorldActions.Depth: 0 when no patched world call is running
        /// (disarm isn't one of them). Only for the disarm.
        public int Depth { get; init; }

        /// WorldActions.Sharing.
        public bool Sharing { get; init; }

        /// This game is the host (Session.Instance.IsHost). Only for the
        /// disarm.
        public bool Host { get; init; }
    }
}
