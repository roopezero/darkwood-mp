using DarkwoodMP.World.Deaths;

namespace DarkwoodMP.Tests.World.Deaths
{
    public class DeathRulesTests
    {
        /// The host's own death with one joiner in the world.
        static readonly DeathFacts HostDying = new DeathFacts
        {
            ClientMode = false,
            IsHost = true,
            Peers = 1,
            InDeath = true,
        };

        public class WorldResetAllowed
        {
            [Fact]
            public void Refuses_the_hosts_death_with_others_in()
            {
                DeathRules.WorldResetAllowed(HostDying).ShouldBeFalse();
            }

            [Fact]
            public void Refuses_a_client_for_any_caller()
            {
                var death = HostDying with
                {
                    ClientMode = true,
                    IsHost = false,
                    Peers = 0,
                    InDeath = false,
                };

                DeathRules.WorldResetAllowed(death).ShouldBeFalse();
            }

            [Fact]
            public void Allows_the_host_outside_a_death()
            {
                var death = HostDying with { InDeath = false };

                DeathRules.WorldResetAllowed(death).ShouldBeTrue();
            }

            [Fact]
            public void Allows_the_host_alone()
            {
                var death = HostDying with { Peers = 0 };

                DeathRules.WorldResetAllowed(death).ShouldBeTrue();
            }

            [Fact]
            public void Allows_a_game_with_no_session()
            {
                var death = HostDying with { IsHost = false };

                DeathRules.WorldResetAllowed(death).ShouldBeTrue();
            }
        }

        public class NeedsStack
        {
            [Fact]
            public void Reads_the_stack_for_a_host_with_peers()
            {
                DeathRules.NeedsStack(HostDying).ShouldBeTrue();
            }

            [Theory]
            [InlineData(true, true, 1)]
            [InlineData(false, false, 1)]
            [InlineData(false, true, 0)]
            public void Skips_the_stack_where_it_changes_nothing(
                bool client,
                bool host,
                int peers)
            {
                var death = HostDying with
                {
                    ClientMode = client,
                    IsHost = host,
                    Peers = peers,
                };

                DeathRules.NeedsStack(death).ShouldBeFalse();
            }
        }
    }
}
