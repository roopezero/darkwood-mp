namespace DarkwoodMP.World.Progress
{
    /// What Progression.Update does on its once-a-second pass.
    public readonly struct ProgressTick
    {
        /// Back at the main menu: forget the world's record and its path,
        /// so a reload reads the file again. Done first.
        public bool Forgets { get; init; }

        /// Then HostSync (Host), Apply (Client) or nothing.
        public ProgressRoute Route { get; init; }
    }
}
