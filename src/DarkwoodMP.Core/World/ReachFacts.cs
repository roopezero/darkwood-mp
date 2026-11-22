namespace DarkwoodMP.World
{
    /// What the host knows about a player and a thing they act on.
    public readonly struct ReachFacts
    {
        /// The host is handling a request (Authority.Handling).
        public bool Handling { get; init; }

        /// The player has a live stand-in (NetPlayerProxy.All): the
        /// requester's for InReach, the named player's for Near.
        public bool Known { get; init; }

        /// Where the stand-in is.
        public Vec2 StandIn { get; init; }

        /// The movement check has a position for the requester.
        public bool Checked { get; init; }

        /// That position (MoveTracker.TryAccepted), when Checked.
        public Vec2 Accepted { get; init; }

        /// Where the thing is.
        public Vec2 Target { get; init; }

        /// How far the player may be from it.
        public float Reach { get; init; }

        /// The thing's name, for the refusal's words.
        public string What { get; init; }
    }
}
