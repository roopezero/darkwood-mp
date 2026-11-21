namespace DarkwoodMP.World.KeptCharacters
{
    /// What a client looks at when its character comes
    /// (Characters.OnLoad).
    public readonly struct LoadFacts
    {
        /// It came from the host, and we're a client.
        public bool FromHost { get; init; }

        public bool Client { get; init; }

        /// We asked, and the scene handle we asked in (_askedScene).
        public bool Asked { get; init; }

        public int AskedScene { get; init; }

        /// SceneManager's active scene handle now, and WorldShare.InWorld.
        public int Scene { get; init; }

        public bool InWorld { get; init; }
    }
}
