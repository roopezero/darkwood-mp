namespace DarkwoodMP.World.Talk
{
    /// What our player's closed dialogue looks at (TalkLock.Closed).
    public readonly struct TalkEndFacts
    {
        /// The NPC kept as ours is there (Unity's null check).
        public bool Ours { get; init; }

        /// The ObjRef kept as ours is valid.
        public bool OursValid { get; init; }

        /// Session.Instance exists.
        public bool HasSession { get; init; }

        /// Session.IsHost.
        public bool IsHost { get; init; }

        /// NetEnemies.ClientMode.
        public bool ClientMode { get; init; }
    }
}
