namespace DarkwoodMP.World.Talk
{
    /// What our player's opened dialogue looks at (TalkLock.Opened).
    public readonly struct TalkOpenFacts
    {
        /// WorldActions.Sharing.
        public bool Sharing { get; init; }

        /// WorldActions.OursAlone of the NPC: a dream's NPC of our own.
        public bool OursAlone { get; init; }

        /// The NPC's ObjRef is valid.
        public bool RefValid { get; init; }

        /// Session.IsHost.
        public bool IsHost { get; init; }
    }
}
