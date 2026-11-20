namespace DarkwoodMP.World.Saves
{
    /// What the delete fence looks at for one folder.
    public readonly struct DeleteFacts
    {
        /// The folder to delete, as the caller gave it: a players' folder's
        /// check takes the save folder here instead.
        public string Path { get; init; }

        /// The HOME variable, and Environment's user profile folder.
        public string Home { get; init; }

        public string UserProfile { get; init; }

        /// BepInEx's cache folder joined with "DarkwoodMP".
        public string CacheRoot { get; init; }

        /// SaveRedirect.Dir: null when saves aren't redirected.
        public string Redirect { get; init; }

        /// Paths compare without case (Windows).
        public bool IgnoreCase { get; init; }

        /// The save slot of a players' folder.
        public int Slot { get; init; }
    }
}
