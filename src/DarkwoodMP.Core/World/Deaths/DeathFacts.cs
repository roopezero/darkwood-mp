namespace DarkwoodMP.World.Deaths
{
    /// What a game knows when the world is about to be reset: every enemy
    /// respawned (WorldGenerator.respawnAllEnemies), the characters
    /// despawned (CharacterSpawner.despawnCharacters) or the night skipped
    /// (Controller.skipDay). A death does all three.
    public readonly struct DeathFacts
    {
        /// This game shows the host's enemies (NetEnemies.ClientMode).
        public bool ClientMode { get; init; }

        /// There is a session and this game is its host.
        public bool IsHost { get; init; }

        /// The players connected to the host (Session.Peers.Count).
        public int Peers { get; init; }

        /// The call comes from inside Player.onDeath (the shell's stack
        /// reader). Looked at only when NeedsStack says so.
        public bool InDeath { get; init; }
    }
}
