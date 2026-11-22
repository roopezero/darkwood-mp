namespace DarkwoodMP.World
{
    /// How far from a thing a player may be to act on it. The limits many
    /// subsystems share (the plugin's WorldActions.Reach and ShotReach) and
    /// the host's two checks of a player's reach (Authority.InReach and
    /// Authority.Near). Distances are on the ground plane.
    public static class ReachRules
    {
        /// How far from a thing a player may be to act on it (generous:
        /// positions arrive a little late).
        public const float Reach = 600f;

        /// How far a player may be from what they shot (the game's shots
        /// reach 1000 px), with the same margin.
        public const float ShotReach = 1200f;

        /// Host, in a request's handler: refuse it when the requesting
        /// player is further than the reach from the target. Allowed when
        /// no request is being handled, or its player has no stand-in yet.
        /// The nearer of the stand-in and the accepted position counts.
        public static Verdict InReach(in ReachFacts reach)
        {
            if (!reach.Handling || !reach.Known)
            {
                return Verdict.Allow;
            }

            float d = Vec2.Distance(reach.StandIn, reach.Target);
            if (d > reach.Reach && reach.Checked)
            {
                d = Min(d, Vec2.Distance(reach.Accepted, reach.Target));
            }

            if (d <= reach.Reach)
            {
                return Verdict.Allow;
            }

            var detail = Refusals.Detail(
                d.ToString("0"),
                reach.Reach.ToString("0"),
                reach.What);
            return Verdict.Refuse(Refusal.OutOfReach, detail);
        }

        /// Host: whether a player's stand-in is within the reach of the
        /// target. True when it has none. Looks at Known, StandIn, Target
        /// and Reach only: no request, no accepted position.
        public static bool Near(in ReachFacts reach)
        {
            if (!reach.Known)
            {
                return true;
            }

            return Vec2.Distance(reach.StandIn, reach.Target) <= reach.Reach;
        }

        /// As Mathf.Min: b unless a is smaller.
        static float Min(float a, float b)
        {
            if (a < b)
            {
                return a;
            }

            return b;
        }
    }
}
