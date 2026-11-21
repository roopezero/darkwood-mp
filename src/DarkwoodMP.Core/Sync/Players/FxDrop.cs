namespace DarkwoodMP.Sync.Players
{
    /// Why one of the effect messages was dropped without a word, before
    /// it's relayed. None means it's relayed and may be shown.
    public enum FxDrop : byte
    {
        None = 0,

        /// No owner: on a client, not from the host, or about our own
        /// player.
        NoOwner,

        /// More sounds than MaxSounds in one batch.
        TooManySounds,

        /// A hurt message of a kind this game doesn't know.
        UnknownKind,
    }
}
