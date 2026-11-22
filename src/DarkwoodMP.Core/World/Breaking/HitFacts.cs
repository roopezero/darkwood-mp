namespace DarkwoodMP.World.Breaking
{
    /// What a game knows about where a hit on an item, door or window
    /// comes from.
    public readonly struct HitFacts
    {
        /// This game is a client (NetEnemies.ClientMode).
        public bool ClientMode { get; init; }

        /// How deep the game is inside a copy of a thrown thing's
        /// explosion (ThrownHits.Depth).
        public int ThrownDepth { get; init; }

        /// A flame is doing damage now (ThrownHits.CurrentFlame).
        public bool Flame { get; init; }

        /// That flame is the local player's
        /// (EnemyAreaHits.ByLocalPlayer).
        public bool FlameOurs { get; init; }

        /// The attacker is the local player's transform.
        public bool AttackerIsPlayer { get; init; }
    }
}
