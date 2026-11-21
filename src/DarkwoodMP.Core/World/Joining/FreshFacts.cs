namespace DarkwoodMP.World.Joining
{
    /// What decides whether the host's last world for joiners is still
    /// fresh (WorldShare.FreshHostWorld).
    public readonly struct FreshFacts
    {
        /// A world was made (_hostWorld), in which scene, when, and with
        /// which stamp of the save files.
        public bool Made { get; init; }

        public int MadeScene { get; init; }

        public Seconds MadeAt { get; init; }

        public string MadeStamp { get; init; }

        /// The active scene's handle, and Time.realtimeSinceStartup.
        public int Scene { get; init; }

        public Seconds Now { get; init; }

        /// The LiveWorld setting.
        public bool LiveWorld { get; init; }

        /// A SaveManager and a current profile, and WorldFiles.Stamp of the
        /// profile's save folder.
        public bool Saves { get; init; }

        public string Stamp { get; init; }
    }
}
