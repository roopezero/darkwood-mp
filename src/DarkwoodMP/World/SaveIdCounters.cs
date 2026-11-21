namespace DarkwoodMP.World
{
    /// <summary>
    /// The save id counters (SaveManager.currentUniqueID) of the saves this
    /// game loaded and wrote (see SharedIds). They live as long as the game
    /// (ModHost.SaveIds): the host loads and saves its world before it
    /// hosts.
    /// </summary>
    internal sealed class SaveIdCounters
    {
        // ... in the last save this game wrote to its own save folder
        private int _saved;
        // ... in the last snapshot the host wrote for a joiner
        private int _snapshot;

        /// The counter in the last save this game loaded.
        public int Loaded { get; private set; }

        /// <summary>
        /// Host: the counter in a snapshot it's about to offer, or in its own
        /// save on disk (fromSnapshot false).
        /// </summary>
        public int CounterOf(bool fromSnapshot)
        {
            if (fromSnapshot)
            {
                return _snapshot;
            }

            return _saved;
        }

        public void OnLoaded(int n)
        {
            Loaded = n;
            _saved = n;
            Plugin.Log.LogInfo(
                $"Loaded a world whose save id counter is at {n}");
        }

        public void OnSaved(int n)
        {
            if (WorldShare.Snapshotting)
            {
                _snapshot = n;
            }
            else
            {
                _saved = n;
            }
        }
    }
}
