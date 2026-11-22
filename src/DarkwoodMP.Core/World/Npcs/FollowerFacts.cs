namespace DarkwoodMP.World.Npcs
{
    /// Host: a character that may follow a joiner.
    public readonly struct FollowerFacts
    {
        /// It has a Character with an npc.
        public bool IsNpc { get; init; }

        /// Character.aggressiveness is follower.
        public bool IsFollower { get; init; }

        /// Character.behaviour is following.
        public bool IsFollowing { get; init; }

        /// A joiner recruited it (NpcFollowers.Recruiter has it).
        public bool Recruited { get; init; }

        /// That joiner's stand-in (NetPlayerProxy.All) exists and is alive.
        public bool LeaderAlive { get; init; }
    }
}
