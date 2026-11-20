namespace DarkwoodMP.World.Npcs
{
    /// Host: a follower a joiner recruited follows that joiner's stand-in,
    /// not the host's player (the plugin's NpcFollowers). The joiner
    /// recruits it with an event step of theirs that makes it a follower.
    public static class FollowerRules
    {
        /// Whether a step makes its targets followers: a modifyCharacter
        /// step that sets aggressiveness to follower or behaviour to
        /// following, or any gameObject step.
        public static bool MakesFollowers(in StepFacts step)
        {
            if (step.ModifiesCharacter)
            {
                return step.SetsAggressiveness && step.ToFollower
                    || step.SetsBehaviour && step.ToFollowing;
            }

            return step.SwitchesObject;
        }

        /// Whether the host looks at a step's targets for recruits: it is
        /// a joiner's, and it makes followers.
        public static bool Recruits(in StepFacts step)
        {
            if (step.By == 0 || !step.Hosting)
            {
                return false;
            }

            return MakesFollowers(step);
        }

        /// Whether one of a recruiting step's targets counts as recruited:
        /// an NPC, and for a gameObject step one that is a follower
        /// already (behaviour following isn't enough).
        public static bool Counts(in StepFacts step, in FollowerFacts target)
        {
            if (!target.IsNpc)
            {
                return false;
            }

            if (step.SwitchesObject && !target.IsFollower)
            {
                return false;
            }

            return true;
        }

        /// Whether a follower follows its recruiter's stand-in (LeaderOf):
        /// recruited, a follower or following now, and the stand-in is
        /// alive. Otherwise it follows the host's player; the record stays.
        public static bool FollowsLeader(in FollowerFacts follower)
        {
            if (!follower.Recruited)
            {
                return false;
            }

            if (!follower.IsFollower && !follower.IsFollowing)
            {
                return false;
            }

            return follower.LeaderAlive;
        }

        /// A follower with a leader, before its sight check: whether it
        /// stops seeing this, every player but the leader.
        public static bool Hides(in SeenFacts seen)
        {
            return seen.Exists && !seen.IsLeader && seen.IsPlayer;
        }

        /// A follower with a leader: whether its target becomes the leader,
        /// when it has none or has another player.
        public static bool Retargets(in SeenFacts target)
        {
            if (!target.Exists)
            {
                return true;
            }

            return !target.IsLeader && target.IsPlayer;
        }
    }
}
