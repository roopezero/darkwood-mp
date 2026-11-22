namespace DarkwoodMP.World.Joining
{
    /// What the host looks at before it snapshots its world for a joiner
    /// (WorldShare.SaveSnapshot).
    public readonly struct SnapshotFacts
    {
        /// A SaveManager, its dontSave, and Core.loadingGame.
        public bool Saves { get; init; }

        public bool DontSave { get; init; }

        public bool Loading { get; init; }

        /// Time.realtimeSinceStartup, and when the last snapshot was made
        /// (-999 before any) in which scene.
        public Seconds Now { get; init; }

        public Seconds LastAt { get; init; }

        public int LastScene { get; init; }

        /// The active scene's handle, and whether the snapshot folder holds
        /// a sav.dat.
        public int Scene { get; init; }

        public bool HasSave { get; init; }
    }
}
