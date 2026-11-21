namespace DarkwoodMP.World.Story
{
    /// What the host knows about a joiner's story step (a StoryRequest,
    /// or the one it holds).
    public readonly struct StoryFacts
    {
        /// The kind the request names.
        public StoryKind Kind { get; init; }

        /// The chapter it names (0 unless Kind is Chapter).
        public int Chapter { get; init; }

        /// The host ran its key already in this session (Chapters._ran
        /// has StoryRequestRules.KeyOf).
        public bool Ran { get; init; }

        /// The host's game is on its way to another part of the story:
        /// _chapterSent, Generating or _endSent.
        public bool OnTheWay { get; init; }

        /// WorldShare.InWorld, Player.Instance is set, not
        /// WorldShare.Loading, and WorldShare.WorldReady.
        public bool HostInWorld { get; init; }

        /// The host's dreams, for StoryRequestRules.HostDreaming.
        public DreamFacts Dreams { get; init; }

        /// The chapter of the host's world (Chapters.HostChapter).
        public int HostChapter { get; init; }

        /// The request's ObjRef is valid and names a GameEvents in the
        /// host's world.
        public bool EventFound { get; init; }

        /// That event's name, for the refusal's words.
        public string EventName { get; init; }

        /// The event's step at the request's index (zero past the end).
        public StoryStepFacts Step { get; init; }

        /// The held request's reach was checked before.
        public bool ReachChecked { get; init; }

        /// The requester has a stand-in (NetPlayerProxy.All).
        public bool Known { get; init; }

        /// Where the stand-in is.
        public Vec2 StandIn { get; init; }

        /// Where the event is (its transform's position).
        public Vec2 EventAt { get; init; }

        /// The requester was away in a place of their own when they asked
        /// (RemotePlayer.Away then).
        public bool WasAway { get; init; }

        /// They are away now (RemotePlayer.Away; false with no remote).
        public bool IsAway { get; init; }
    }
}
