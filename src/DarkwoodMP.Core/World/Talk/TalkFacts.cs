namespace DarkwoodMP.World.Talk
{
    /// Host: what it looks at when a joiner claims an NPC to talk to
    /// (TalkLock with talking 1).
    public readonly struct TalkFacts
    {
        /// The peer that claims it.
        public ulong From { get; init; }

        /// Someone holds the lock key, who, and their name
        /// (Session.NameOf).
        public bool Held { get; init; }

        public ulong Holder { get; init; }

        public string HolderName { get; init; }

        /// Someone let go of it before, who, their name, and when
        /// (unscaled).
        public bool Released { get; init; }

        public ulong Releaser { get; init; }

        public string ReleaserName { get; init; }

        public Seconds ReleasedAt { get; init; }

        /// Time.unscaledTime.
        public Seconds Now { get; init; }

        /// The NPC is in the host's world (ObjRef.Resolve).
        public bool Resolved { get; init; }

        /// The claimer and the NPC, when Resolved, as Authority.InReach
        /// has them: What is the NPC's name. Its Reach is TalkRules.Reach,
        /// whatever it holds.
        public ReachFacts Reach { get; init; }
    }
}
