using DarkwoodMP.World.Scripted;

namespace DarkwoodMP.Tests.World.Scripted
{
    public class DoerRulesTests
    {
        /// Sharing in a loaded world, nothing being run, a type with no
        /// rule of its own.
        static readonly DoerFacts Sharing = new DoerFacts
        {
            Active = true,
            InWorld = true,
            Sharing = true,
        };

        /// Our own action, done here as the host decided, on a door.
        static readonly DoerFacts Own = Sharing with
        {
            OwnAction = true,
            ActionType = true,
        };

        public class ByOurPlayer
        {
            [Fact]
            public void Asks_the_stack_about_an_ordinary_type()
            {
                DoerRules.ByOurPlayer(Sharing).ShouldBe(Doer.Stack);
            }

            [Fact]
            public void Takes_our_own_action_as_ours()
            {
                DoerRules.ByOurPlayer(Own).ShouldBe(Doer.Yes);
            }

            [Fact]
            public void Takes_our_own_hit_as_ours()
            {
                var f = Own with { ActionType = false, HitType = true };

                DoerRules.ByOurPlayer(f).ShouldBe(Doer.Yes);
            }

            [Fact]
            public void Takes_our_own_action_as_ours_while_replaying()
            {
                var f = Own with
                {
                    Replaying = true,
                    Current = EventPart.SharedOnly,
                    Sharing = false,
                };

                DoerRules.ByOurPlayer(f).ShouldBe(Doer.Yes);
            }

            [Theory]
            [InlineData(false, true, false)]
            [InlineData(true, false, false)]
            [InlineData(true, true, true)]
            public void Takes_no_own_action_outside_a_loaded_world(
                bool active,
                bool world,
                bool loading)
            {
                var f = Own with
                {
                    Active = active,
                    InWorld = world,
                    Loading = loading,
                };

                DoerRules.ByOurPlayer(f).ShouldBe(Doer.No);
            }

            [Fact]
            public void Asks_the_stack_about_an_own_action_of_another_type()
            {
                var f = Own with { ActionType = false };

                DoerRules.ByOurPlayer(f).ShouldBe(Doer.Stack);
            }

            [Fact]
            public void Takes_no_replay_as_ours()
            {
                var f = Sharing with { Replaying = true, Sight = true };

                DoerRules.ByOurPlayer(f).ShouldBe(Doer.No);
            }

            [Fact]
            public void Takes_nothing_in_a_part_being_run_as_ours()
            {
                var f = Sharing with
                {
                    Current = EventPart.PlayerAndShared,
                    Sight = true,
                };

                DoerRules.ByOurPlayer(f).ShouldBe(Doer.No);
            }

            [Fact]
            public void Takes_nothing_as_ours_when_not_sharing()
            {
                var f = Sharing with { Sharing = false, Sight = true };

                DoerRules.ByOurPlayer(f).ShouldBe(Doer.No);
            }

            [Fact]
            public void Takes_a_type_the_world_sets_off_as_not_ours()
            {
                var f = Sharing with { WorldType = true, Sight = true };

                DoerRules.ByOurPlayer(f).ShouldBe(Doer.No);
            }

            [Fact]
            public void Takes_an_area_our_player_walked_into_as_ours()
            {
                var f = Sharing with { Area = true, PlayerEntered = true };

                DoerRules.ByOurPlayer(f).ShouldBe(Doer.Yes);
            }

            [Fact]
            public void Takes_an_area_as_ours_when_the_test_acts_as_us()
            {
                var f = Sharing with { Area = true, TestAsPlayer = true };

                DoerRules.ByOurPlayer(f).ShouldBe(Doer.Yes);
            }

            [Fact]
            public void Takes_an_area_someone_else_walked_into_as_not_ours()
            {
                var f = Sharing with { Area = true };

                DoerRules.ByOurPlayer(f).ShouldBe(Doer.No);
            }

            [Fact]
            public void Takes_sight_as_ours()
            {
                var f = Sharing with { Sight = true };

                DoerRules.ByOurPlayer(f).ShouldBe(Doer.Yes);
            }
        }

        public class LeftToActor
        {
            static readonly DoerFacts Leaving = Sharing with
            {
                Leaving = true,
                LeftType = true,
            };

            [Fact]
            public void Leaves_a_type_of_the_action_to_the_actor()
            {
                DoerRules.LeftToActor(Leaving).ShouldBeTrue();
            }

            [Fact]
            public void Keeps_a_type_outside_the_action()
            {
                var f = Leaving with { LeftType = false };

                DoerRules.LeftToActor(f).ShouldBeFalse();
            }

            [Fact]
            public void Keeps_our_own_action()
            {
                var f = Leaving with { OwnAction = true };

                DoerRules.LeftToActor(f).ShouldBeFalse();
            }

            [Fact]
            public void Keeps_everything_when_no_action_is_applied()
            {
                var f = Leaving with { Leaving = false };

                DoerRules.LeftToActor(f).ShouldBeFalse();
            }
        }

        public class CountsLeft
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(true, true, false)]
            [InlineData(false, false, false)]
            public void Counts_only_a_matching_trigger_that_is_on(
                bool matches,
                bool disabled,
                bool counts)
            {
                DoerRules.CountsLeft(matches, disabled).ShouldBe(counts);
            }
        }

        public class SharedReplay
        {
            static readonly DoerFacts Replay = Sharing with
            {
                Replaying = true,
                ActionType = true,
                Client = true,
            };

            [Fact]
            public void Takes_the_hosts_action_applied_here_as_shared()
            {
                DoerRules.SharedReplay(Replay).ShouldBeTrue();
            }

            [Fact]
            public void Skips_a_hit()
            {
                var f = Replay with { ActionType = false, HitType = true };

                DoerRules.SharedReplay(f).ShouldBeFalse();
            }

            [Fact]
            public void Skips_the_host()
            {
                var f = Replay with { Client = false };

                DoerRules.SharedReplay(f).ShouldBeFalse();
            }

            [Fact]
            public void Skips_what_isnt_a_replay()
            {
                var f = Replay with { Replaying = false };

                DoerRules.SharedReplay(f).ShouldBeFalse();
            }

            [Fact]
            public void Skips_another_players_action()
            {
                var f = Replay with { Leaving = true };

                DoerRules.SharedReplay(f).ShouldBeFalse();
            }

            [Fact]
            public void Skips_our_own_action()
            {
                var f = Replay with { OwnAction = true };

                DoerRules.SharedReplay(f).ShouldBeFalse();
            }
        }

        public class CauseFor
        {
            static readonly CauseFacts Death = new CauseFacts
            {
                Cause = 76561198000000001UL,
                Host = true,
                Present = true,
                Caused = true,
            };

            [Fact]
            public void Names_the_client_behind_a_death()
            {
                DoerRules.CauseFor(Death).ShouldBe(76561198000000001UL);
            }

            [Fact]
            public void Names_nobody_for_a_type_no_player_causes()
            {
                var f = Death with { Caused = false };

                DoerRules.CauseFor(f).ShouldBe(0UL);
            }

            [Fact]
            public void Names_nobody_who_has_gone()
            {
                var f = Death with { Present = false };

                DoerRules.CauseFor(f).ShouldBe(0UL);
            }

            [Fact]
            public void Names_nobody_on_a_client()
            {
                var f = Death with { Host = false };

                DoerRules.CauseFor(f).ShouldBe(0UL);
            }

            [Fact]
            public void Names_nobody_without_a_cause()
            {
                var f = Death with { Cause = 0 };

                DoerRules.CauseFor(f).ShouldBe(0UL);
            }
        }
    }
}
