namespace DarkwoodMP.World.Culling
{
    /// The grid a remote player is in (CullingRules.GridOf).
    public enum PlayerGrid : byte
    {
        /// The world's grid.
        World = 0,

        /// Their separate location's grid (by its true location name).
        Location,

        /// None: away in a place the host doesn't run.
        None,
    }
}
