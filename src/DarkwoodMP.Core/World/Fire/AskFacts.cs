namespace DarkwoodMP.World.Fire
{
    /// What a client knows about a thing its player's flame set alight
    /// (FireSync.ClientIgnited).
    public readonly struct AskFacts
    {
        /// The thing is there (not null).
        public bool Exists { get; init; }

        /// This game is a client (NetEnemies.ClientMode).
        public bool ClientMode { get; init; }

        /// WorldActions.Replaying.
        public bool Replaying { get; init; }

        /// It is in a place of our own (WorldActions.OursAlone).
        public bool OursAlone { get; init; }

        /// What it is (FireRules.KindOf).
        public FireKind Kind { get; init; }

        /// It has a valid ObjRef.
        public bool Named { get; init; }
    }
}
