namespace DarkwoodMP.World.Pause
{
    /// What the pause's every-frame check looks at.
    public readonly struct PauseFacts
    {
        /// Others share this world (PauseRules.InCoop).
        public bool InCoop { get; init; }

        /// The game is marked paused (Core.Paused).
        public bool Paused { get; init; }

        /// Time.timeScale.
        public float TimeScale { get; init; }

        /// Our player exists and is alive (Player.Instance.alive).
        public bool Alive { get; init; }

        /// The player has a Rigidbody.
        public bool HasBody { get; init; }

        /// That Rigidbody is kinematic.
        public bool Kinematic { get; init; }
    }
}
