using DarkwoodMP.World;

namespace DarkwoodMP.Tests.World
{
    /// A requesting player at the origin and a thing some distance east of
    /// them, for the rules that check a reach.
    static class Requesters
    {
        /// Right at ReachRules.Reach: still in reach.
        public static readonly ReachFacts AtReach = At(ReachRules.Reach);

        /// Just past ReachRules.Reach.
        public static readonly ReachFacts PastReach =
            At(ReachRules.Reach + 0.5f);

        /// Right at ReachRules.ShotReach: still in reach.
        public static readonly ReachFacts AtShotReach =
            At(ReachRules.ShotReach);

        /// Just past ReachRules.ShotReach.
        public static readonly ReachFacts PastShotReach =
            At(ReachRules.ShotReach + 0.5f);

        public static ReachFacts At(float distance)
        {
            return new ReachFacts
            {
                Handling = true,
                Known = true,
                StandIn = new Vec2(0f, 0f),
                Target = new Vec2(distance, 0f),
                What = "thing",
            };
        }
    }
}
