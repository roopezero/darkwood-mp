namespace DarkwoodMP.Combat.Enemies
{
    /// What a copy's sprite animator is playing and what the host's plays
    /// (the plugin's NetEnemies.ApplyAnim).
    public readonly struct ClipFacts
    {
        /// The host's clip (StableHash of its name; 0 for none).
        public int Clip { get; init; }

        /// The host's frame.
        public int Frame { get; init; }

        /// The animator has a Library.
        public bool Library { get; init; }

        /// Its CurrentClip isn't null and has the host's clip's hash.
        public bool Same { get; init; }

        /// Its CurrentFrame.
        public int Current { get; init; }

        /// Its CurrentClip has frames (they aren't null).
        public bool HasFrames { get; init; }

        /// How many, when HasFrames.
        public int Frames { get; init; }
    }
}
