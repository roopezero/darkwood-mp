namespace DarkwoodMP.World.Dragging
{
    /// What the host knows when a client takes hold of a thing (a DragClaim
    /// inside a Request).
    public readonly struct ClaimFacts
    {
        /// This game is the host (Session.IsHost).
        public bool Host { get; init; }

        /// A request is being handled (Authority.Handling).
        public bool Handling { get; init; }

        /// The thing is in the host's world (ObjRef.Resolve of an Item).
        public bool Found { get; init; }

        /// The claimer's reach to the thing (ReachRules.InReach, its target
        /// the thing's place, ReachRules.Reach, the item's name), when Found.
        public Verdict Reach { get; init; }

        /// The owner table has an entry for the thing.
        public bool Owned { get; init; }

        /// Who drags it, when Owned.
        public ulong Owner { get; init; }

        /// That owner's name (Session.NameOf): the refusal's detail. Read
        /// only when Owned and Owner isn't From, so the owner claiming
        /// again builds no name; a stale owner's name is still built, which
        /// is taken on purpose: claims are rare and keep the Verdict whole.
        public string OwnerName { get; init; }

        /// When the owner was last heard of, when Owned.
        public Seconds Heard { get; init; }

        /// Time.unscaledTime.
        public Seconds Now { get; init; }

        /// Who asks (the request's sender).
        public ulong From { get; init; }
    }
}
