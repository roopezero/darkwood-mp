namespace DarkwoodMP.World.Infections
{
    /// What the host knows when an infection patch starts (Infection.init).
    public readonly struct SpawnFacts
    {
        /// How deep the game is inside Infection.spawnInfection or
        /// Character.waitToSpawnEgg (InfectionSync.Growing).
        public int Growing { get; init; }

        /// The patch is there (not null).
        public bool Exists { get; init; }

        /// There is a session and this game is its host.
        public bool Host { get; init; }

        /// Coop.Guards.Sharing.
        public bool Sharing { get; init; }
    }
}
