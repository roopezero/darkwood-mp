namespace DarkwoodMP.World.Culling
{
    /// Which refresh of the world's culling grid runs
    /// (CullingRules.Which).
    public enum RefreshKind : byte
    {
        /// The game's own WorldGrid.refreshPosition.
        Game = 0,

        /// Ours round our player alone: as the game's, but a new pass
        /// stops the grid's old one.
        OwnPass,

        /// Ours round every player, on every grid a player is in, then
        /// World.FarEvents.Tick.
        Shared,
    }
}
