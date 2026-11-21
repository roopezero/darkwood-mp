namespace DarkwoodMP.World.Breaking
{
    /// What a game knows about an explosive as it goes off
    /// (Explodes.onActivate).
    public readonly struct ExplodeFacts
    {
        /// The explosive is there (not null).
        public bool Exists { get; init; }

        /// It has gone off already (Explodes.activated).
        public bool Activated { get; init; }

        /// It is a copy of another game's item (a ProxyItem).
        public bool Proxy { get; init; }

        /// It was thrown (a ThrownItem), or is a copy of a thrown one
        /// (ThrownHits.Copies): ThrowSync's.
        public bool Thrown { get; init; }

        /// It is a Character: a kamikaze.
        public bool Character { get; init; }

        /// An enemy threw or spawned it (EnemyAreaHits.ByEnemy).
        public bool ByEnemy { get; init; }
    }
}
