namespace DarkwoodMP.World.Npcs
{
    /// Host: something a recruited follower sees or targets, for its sight
    /// check.
    public readonly struct SeenFacts
    {
        /// Seen: the collider has a CharBase. Target: it has a target.
        public bool Exists { get; init; }

        /// It is the follower's leader: the CharBase is the leader's
        /// stand-in, or the target is its transform.
        public bool IsLeader { get; init; }

        /// Its CharBase is a player (Proxies.IsPlayer: our Player or a
        /// NetPlayerProxy).
        public bool IsPlayer { get; init; }
    }
}
