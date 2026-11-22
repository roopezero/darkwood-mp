namespace DarkwoodMP.World.Inventories
{
    /// How Containers.Apply loads contents that came from another game.
    public enum LoadPath : byte
    {
        /// Closed here: load them.
        Load = 0,

        /// Open, with the same number of slots, and the window redraws:
        /// load them into the open window (ApplyOpen).
        InWindow,

        /// Open with another number of slots, or without redraws: close the
        /// window, then load them.
        CloseFirst,
    }
}
