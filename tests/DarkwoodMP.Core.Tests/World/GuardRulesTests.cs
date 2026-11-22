using DarkwoodMP.World;

namespace DarkwoodMP.Tests.World
{
    public class GuardRulesTests
    {
        /// A host with one player in, in a loaded world.
        static readonly GuardFacts Hosting = new GuardFacts
        {
            InWorld = true,
            Host = true,
            Peers = 1,
        };

        /// A client in the host's world.
        static readonly GuardFacts Joined = new GuardFacts
        {
            InWorld = true,
            Client = true,
        };

        public class IsOutermost
        {
            [Fact]
            public void Counts_the_only_call_as_outermost()
            {
                GuardRules.IsOutermost(1).ShouldBeTrue();
            }

            [Fact]
            public void Refuses_a_call_inside_another()
            {
                GuardRules.IsOutermost(2).ShouldBeFalse();
            }

            [Fact]
            public void Counts_a_depth_that_went_wrong_as_outermost()
            {
                var depths = new[] { 0, -3 };

                depths.ShouldAllBe(d => GuardRules.IsOutermost(d));
            }
        }

        public class Leave
        {
            [Fact]
            public void Takes_one_call_off()
            {
                GuardRules.Leave(3).ShouldBe(2);
            }

            [Fact]
            public void Ends_at_none_after_the_outermost()
            {
                GuardRules.Leave(1).ShouldBe(0);
            }

            [Fact]
            public void Puts_a_depth_that_went_wrong_back_to_none()
            {
                var left = new[] { GuardRules.Leave(0), GuardRules.Leave(-3) };

                left.ShouldBe(new[] { 0, 0 });
            }
        }

        public class Sharing
        {
            [Fact]
            public void Shares_as_a_host_with_a_player_in()
            {
                GuardRules.Sharing(Hosting).ShouldBeTrue();
            }

            [Fact]
            public void Shares_as_a_client_in_the_host_world()
            {
                GuardRules.Sharing(Joined).ShouldBeTrue();
            }

            [Fact]
            public void Refuses_a_host_with_nobody_in()
            {
                var alone = Hosting with { Peers = 0 };

                GuardRules.Sharing(alone).ShouldBeFalse();
            }

            [Fact]
            public void Refuses_while_replaying()
            {
                var replaying = Joined with { Replaying = true };

                GuardRules.Sharing(replaying).ShouldBeFalse();
            }

            [Fact]
            public void Refuses_outside_a_world()
            {
                var menu = Hosting with { InWorld = false };

                GuardRules.Sharing(menu).ShouldBeFalse();
            }

            [Fact]
            public void Refuses_while_the_world_loads()
            {
                var loading = Joined with { Loading = true };

                GuardRules.Sharing(loading).ShouldBeFalse();
            }

            [Fact]
            public void Refuses_a_game_that_neither_hosts_nor_joined()
            {
                var neither = new GuardFacts { InWorld = true, Peers = 2 };

                GuardRules.Sharing(neither).ShouldBeFalse();
            }
        }
    }
}
