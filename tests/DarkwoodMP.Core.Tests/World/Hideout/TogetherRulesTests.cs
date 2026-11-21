using DarkwoodMP.World.Hideout;

namespace DarkwoodMP.Tests.World.Hideout
{
    public class TogetherRulesTests
    {
        /// A host in a loaded world with one joiner.
        static readonly TogetherFacts Hosting = new TogetherFacts
        {
            HasSession = true,
            InWorld = true,
            Loading = false,
            IsHost = true,
            Peers = 1,
            ClientMode = false,
        };

        public class Together
        {
            [Fact]
            public void Counts_a_host_with_a_joiner()
            {
                TogetherRules.Together(Hosting).ShouldBeTrue();
            }

            [Fact]
            public void Counts_a_client()
            {
                var facts = Hosting with
                {
                    IsHost = false,
                    Peers = 0,
                    ClientMode = true,
                };

                TogetherRules.Together(facts).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_out_a_host_alone()
            {
                var facts = Hosting with { Peers = 0 };

                TogetherRules.Together(facts).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_world_loading()
            {
                var facts = Hosting with { Loading = true };

                TogetherRules.Together(facts).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_game_with_no_world()
            {
                var facts = Hosting with { InWorld = false };

                TogetherRules.Together(facts).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_game_with_no_session()
            {
                var facts = Hosting with { HasSession = false };

                TogetherRules.Together(facts).ShouldBeFalse();
            }
        }
    }
}
