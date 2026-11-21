namespace DarkwoodMP.World.KeptCharacters
{
    /// What a client's game looks at about its own character in the host's
    /// world (Characters' client half).
    public readonly struct OurFacts
    {
        /// NetEnemies.ClientMode and WorldShare.InWorld.
        public bool Client { get; init; }

        public bool InWorld { get; init; }

        /// Our character has come (CharacterLoad).
        public bool Have { get; init; }

        /// When the world became ready (Time.unscaledTime); below zero
        /// while it isn't.
        public Seconds ReadyAt { get; init; }

        /// Time.unscaledTime.
        public Seconds Now { get; init; }

        /// The quit's save was sent (SaveForQuit).
        public bool QuitSent { get; init; }

        /// Prologue.Active.
        public bool Prologue { get; init; }
    }
}
