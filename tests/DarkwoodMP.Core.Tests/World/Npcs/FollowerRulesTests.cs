using DarkwoodMP.World.Npcs;

namespace DarkwoodMP.Tests.World.Npcs
{
    public class FollowerRulesTests
    {
        /// A joiner's step, on the host, that sets aggressiveness to
        /// follower.
        static readonly StepFacts ToFollower = new StepFacts
        {
            Hosting = true,
            By = 7,
            ModifiesCharacter = true,
            SetsAggressiveness = true,
            ToFollower = true,
        };

        /// A joiner's step that sets behaviour to following.
        static readonly StepFacts ToFollowing = new StepFacts
        {
            Hosting = true,
            By = 7,
            ModifiesCharacter = true,
            SetsBehaviour = true,
            ToFollowing = true,
        };

        /// A joiner's step that switches on objects.
        static readonly StepFacts SwitchOn = new StepFacts
        {
            Hosting = true,
            By = 7,
            SwitchesObject = true,
        };

        /// A recruited follower whose leader's stand-in is alive.
        static readonly FollowerFacts Follower = new FollowerFacts
        {
            IsNpc = true,
            IsFollower = true,
            Recruited = true,
            LeaderAlive = true,
        };

        /// Another player than the leader.
        static readonly SeenFacts Other = new SeenFacts
        {
            Exists = true,
            IsPlayer = true,
        };

        public class MakesFollowers
        {
            [Fact]
            public void Takes_a_step_that_makes_a_follower()
            {
                var makes = FollowerRules.MakesFollowers(ToFollower);

                makes.ShouldBeTrue();
            }

            [Fact]
            public void Takes_a_step_that_makes_one_follow()
            {
                var makes = FollowerRules.MakesFollowers(ToFollowing);

                makes.ShouldBeTrue();
            }

            [Fact]
            public void Takes_any_step_that_switches_on_objects()
            {
                var makes = FollowerRules.MakesFollowers(SwitchOn);

                makes.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_aggressiveness_set_to_something_else()
            {
                var step = ToFollower with { ToFollower = false };

                var makes = FollowerRules.MakesFollowers(step);

                makes.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_behaviour_set_to_something_else()
            {
                var step = ToFollowing with { ToFollowing = false };

                var makes = FollowerRules.MakesFollowers(step);

                makes.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_follower_value_of_another_field()
            {
                var step = ToFollower with
                {
                    SetsAggressiveness = false,
                    SetsBehaviour = true,
                };

                var makes = FollowerRules.MakesFollowers(step);

                makes.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_any_other_step()
            {
                var step = SwitchOn with { SwitchesObject = false };

                var makes = FollowerRules.MakesFollowers(step);

                makes.ShouldBeFalse();
            }
        }

        public class Recruits
        {
            [Fact]
            public void Looks_at_a_joiner_step_on_the_host()
            {
                var recruits = FollowerRules.Recruits(ToFollower);

                recruits.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_the_host_own_step()
            {
                var step = ToFollower with { By = 0 };

                var recruits = FollowerRules.Recruits(step);

                recruits.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_it_on_a_client()
            {
                var step = ToFollower with { Hosting = false };

                var recruits = FollowerRules.Recruits(step);

                recruits.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_step_that_makes_no_followers()
            {
                var step = ToFollower with { ModifiesCharacter = false };

                var recruits = FollowerRules.Recruits(step);

                recruits.ShouldBeFalse();
            }
        }

        public class Counts
        {
            [Fact]
            public void Counts_an_npc_a_step_makes_a_follower()
            {
                var target = Follower with { IsFollower = false };

                var counts = FollowerRules.Counts(ToFollower, target);

                counts.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_a_target_that_is_no_npc()
            {
                var target = Follower with { IsNpc = false };

                var counts = FollowerRules.Counts(ToFollower, target);

                counts.ShouldBeFalse();
            }

            [Fact]
            public void Counts_a_follower_switched_on()
            {
                var counts = FollowerRules.Counts(SwitchOn, Follower);

                counts.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_a_following_npc_switched_on()
            {
                var target = Follower with
                {
                    IsFollower = false,
                    IsFollowing = true,
                };

                var counts = FollowerRules.Counts(SwitchOn, target);

                counts.ShouldBeFalse();
            }
        }

        public class FollowsLeader
        {
            [Fact]
            public void Follows_the_leader_of_a_recruited_follower()
            {
                var follows = FollowerRules.FollowsLeader(Follower);

                follows.ShouldBeTrue();
            }

            [Fact]
            public void Follows_the_leader_of_one_only_following()
            {
                var follower = Follower with
                {
                    IsFollower = false,
                    IsFollowing = true,
                };

                var follows = FollowerRules.FollowsLeader(follower);

                follows.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_one_nobody_recruited()
            {
                var follower = Follower with { Recruited = false };

                var follows = FollowerRules.FollowsLeader(follower);

                follows.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_one_that_no_longer_follows()
            {
                var follower = Follower with { IsFollower = false };

                var follows = FollowerRules.FollowsLeader(follower);

                follows.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_one_whose_leader_is_dead()
            {
                var follower = Follower with { LeaderAlive = false };

                var follows = FollowerRules.FollowsLeader(follower);

                follows.ShouldBeFalse();
            }
        }

        public class Hides
        {
            [Fact]
            public void Hides_another_player()
            {
                var hides = FollowerRules.Hides(Other);

                hides.ShouldBeTrue();
            }

            [Fact]
            public void Keeps_the_leader()
            {
                var seen = Other with { IsLeader = true };

                var hides = FollowerRules.Hides(seen);

                hides.ShouldBeFalse();
            }

            [Fact]
            public void Keeps_a_character_that_is_no_player()
            {
                var seen = Other with { IsPlayer = false };

                var hides = FollowerRules.Hides(seen);

                hides.ShouldBeFalse();
            }

            [Fact]
            public void Keeps_a_collider_with_no_character()
            {
                var seen = Other with { Exists = false };

                var hides = FollowerRules.Hides(seen);

                hides.ShouldBeFalse();
            }
        }

        public class Retargets
        {
            [Fact]
            public void Retargets_one_after_another_player()
            {
                var retargets = FollowerRules.Retargets(Other);

                retargets.ShouldBeTrue();
            }

            [Fact]
            public void Retargets_one_with_no_target()
            {
                var target = Other with { Exists = false, IsPlayer = false };

                var retargets = FollowerRules.Retargets(target);

                retargets.ShouldBeTrue();
            }

            [Fact]
            public void Keeps_the_leader_as_target()
            {
                var target = Other with { IsLeader = true };

                var retargets = FollowerRules.Retargets(target);

                retargets.ShouldBeFalse();
            }

            [Fact]
            public void Keeps_a_target_that_is_no_player()
            {
                var target = Other with { IsPlayer = false };

                var retargets = FollowerRules.Retargets(target);

                retargets.ShouldBeFalse();
            }
        }
    }
}
