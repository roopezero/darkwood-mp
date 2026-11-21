namespace DarkwoodMP.World.Traps
{
    /// What a client knows about what touched a trap
    /// (Trigger.checkCollision), to say whether the trap ignores it.
    public readonly struct TouchFacts
    {
        /// NetEnemies.ClientMode: the host runs the enemies.
        public bool ClientMode { get; init; }

        /// The collider isn't null.
        public bool HasCollider { get; init; }

        /// The collider has a Character.
        public bool Character { get; init; }

        /// That character is one of our frozen copies (NetEnemies.Frozen).
        public bool Frozen { get; init; }

        /// That character is a puppet of the host's (NetEnemies.IsPuppet).
        public bool Puppet { get; init; }
    }
}
