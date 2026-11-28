using DarkwoodMP.World.Dreaming;

namespace DarkwoodMP.Tests.World.Dreaming
{
    public class StoryDreamRulesTests
    {
        /// The host, awake, and the dream is the cellar's.
        static readonly StoryDreamFacts Host = new StoryDreamFacts
        {
            HasSession = true,
            IsHost = true,
            Together = true,
        };

        /// A client in the host's world, and the dream is the cellar's.
        static readonly StoryDreamFacts Client = new StoryDreamFacts
        {
            HasSession = true,
            ClientMode = true,
            Together = true,
        };

        public class Together
        {
            [Fact]
            public void Takes_the_village_cellars_dream()
            {
                StoryDreamRules.Together("dream_village_cellar")
                    .ShouldBeTrue();
            }

            [Theory]
            [InlineData("dream_doctor_01")]
            [InlineData("dream_church_ruins_01")]
            [InlineData("epilog_part1a_dream")]
            [InlineData("")]
            [InlineData(null)]
            public void Leaves_other_dreams_to_the_player(string preset)
            {
                StoryDreamRules.Together(preset).ShouldBeFalse();
            }
        }

        public class Drawn
        {
            [Fact]
            public void Shares_the_hosts_dream_with_everyone()
            {
                StoryDreamRules.Drawn(Host).ShouldBe(StoryStep.Share);
            }

            [Fact]
            public void Asks_the_host_for_a_clients_dream()
            {
                StoryDreamRules.Drawn(Client).ShouldBe(StoryStep.Ask);
            }

            public static TheoryData<StoryDreamFacts> Ignored =>
                new TheoryData<StoryDreamFacts>
                {
                    Host with { HasSession = false },
                    Host with { Together = false },
                    Host with { Shared = true },
                    Host with { LoadingGame = true },
                    Host with { EpiloguePlaying = true },
                    Client with { Shared = true },
                    Client with { ClientMode = false },
                };

            [Theory]
            [MemberData(nameof(Ignored))]
            public void Does_nothing_for_a_dream_not_to_share(
                StoryDreamFacts dream)
            {
                StoryDreamRules.Drawn(dream).ShouldBe(StoryStep.None);
            }
        }

        public class Asked
        {
            [Fact]
            public void Follows_a_joiner_into_its_dream()
            {
                StoryDreamRules.Asked(Host).ShouldBe(StoryStep.Follow);
            }

            [Fact]
            public void Confirms_the_dream_shared_already()
            {
                var dream = Host with { Shared = true, SameDream = true };

                StoryDreamRules.Asked(dream).ShouldBe(StoryStep.Confirm);
            }

            public static TheoryData<StoryDreamFacts> Refused =>
                new TheoryData<StoryDreamFacts>
                {
                    Host with { HasSession = false },
                    Host with { IsHost = false },
                    Host with { Together = false },
                    Host with { Shared = true },
                    Host with { InDream = true },
                    Host with { LoadingGame = true },
                    Host with { EpiloguePlaying = true },
                };

            [Theory]
            [MemberData(nameof(Refused))]
            public void Leaves_the_joiners_dream_its_own_when_it_cant_follow(
                StoryDreamFacts dream)
            {
                StoryDreamRules.Asked(dream).ShouldBe(StoryStep.None);
            }
        }
    }
}
