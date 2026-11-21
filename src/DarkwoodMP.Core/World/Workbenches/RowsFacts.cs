namespace DarkwoodMP.World.Workbenches
{
    /// What WorkbenchStorage.Update looks at each frame.
    public readonly struct RowsFacts
    {
        /// A world is loaded and ready (WorldShare.WorldReady).
        public bool Ready { get; init; }

        /// The active scene's handle (SceneManager.GetActiveScene().handle).
        public int Scene { get; init; }

        /// Controller.workbenchLevel, or WorkbenchRules.BaseLevel when
        /// there is no Controller.
        public int Level { get; init; }
    }
}
