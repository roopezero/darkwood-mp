namespace DarkwoodMP.Sync.Players
{
    /// Why a PlayerState was dropped without a word. None means it's
    /// shown. In the order PlayerSync.OnPlayerState checks them.
    public enum StateDrop : byte
    {
        None = 0,

        /// Its player left less than LeftIgnore ago: over Steam a state
        /// sent before PlayerLeft can arrive after it.
        LeftJustNow,

        /// A client takes states only from the host.
        NotFromHost,

        /// It's about our own player.
        AboutUs,
    }
}
