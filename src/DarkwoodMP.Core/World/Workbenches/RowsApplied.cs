namespace DarkwoodMP.World.Workbenches
{
    /// The scene and level the workbenches were last given their rows in
    /// (WorkbenchStorage._appliedScene and _appliedLevel).
    public readonly struct RowsApplied
    {
        /// The scene handle that stands for "no scene yet".
        public const int NoScene = -1;

        /// The state before any world: no scene, no level.
        public static readonly RowsApplied None = new RowsApplied
        {
            Scene = NoScene,
            Level = -1,
        };

        /// The active scene's handle, or NoScene.
        public int Scene { get; init; }

        /// Controller.workbenchLevel when the rows were given.
        public int Level { get; init; }
    }
}
