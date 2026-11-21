namespace DarkwoodMP.World.Workbenches
{
    /// Why every workbench is given its rows now (the "why" the plugin's
    /// WorkbenchStorage.ApplyAll logs).
    public enum RowsWhy
    {
        /// Nothing changed.
        None,

        /// A world became ready, or another scene is active: "world
        /// loaded". It also logs the storages that didn't grow.
        WorldLoaded,

        /// The level changed in the same scene: "level changed".
        LevelChanged,
    }
}
