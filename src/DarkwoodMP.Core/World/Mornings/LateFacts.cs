namespace DarkwoodMP.World.Mornings
{
    /// What the host's late-morning check looks at each frame.
    public readonly struct LateFacts
    {
        /// PlayerSync.Instance exists.
        public bool HasSync { get; init; }

        /// WorldShare.InWorld.
        public bool InWorld { get; init; }

        /// Controller.day.
        public int Today { get; init; }
    }
}
