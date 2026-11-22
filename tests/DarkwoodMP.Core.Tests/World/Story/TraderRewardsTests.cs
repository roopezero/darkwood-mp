using DarkwoodMP.World.Story;

namespace DarkwoodMP.Tests.World.Story
{
    public class TraderRewardsTests
    {
        /// The trader got 10 on day 3 in this game.
        static TraderRewards GotTenOnDayThree()
        {
            var rewards = new TraderRewards();
            rewards.Local("Trader", 3, 10);
            return rewards;
        }

        public class Local
        {
            [Fact]
            public void Sends_the_first_reward_of_the_day()
            {
                var rewards = new TraderRewards();

                var outcome = rewards.Local("Trader", 3, 10);

                outcome.ShouldBe(new RewardOutcome
                {
                    Kind = RewardKind.Sent,
                });
            }

            [Fact]
            public void Takes_back_a_second_one_that_day()
            {
                var rewards = GotTenOnDayThree();

                var outcome = rewards.Local("Trader", 3, 12);

                outcome.ShouldBe(new RewardOutcome
                {
                    Kind = RewardKind.TakenBack,
                    Change = -12,
                    Had = 10,
                });
            }

            [Fact]
            public void Sends_the_next_days_reward()
            {
                var rewards = GotTenOnDayThree();

                var outcome = rewards.Local("Trader", 4, 12);

                outcome.Kind.ShouldBe(RewardKind.Sent);
            }

            [Fact]
            public void Sends_another_traders_reward()
            {
                var rewards = GotTenOnDayThree();

                var outcome = rewards.Local("Doctor", 3, 12);

                outcome.Kind.ShouldBe(RewardKind.Sent);
            }

            [Fact]
            public void Ignores_no_amount()
            {
                var rewards = new TraderRewards();

                var outcome = rewards.Local("Trader", 3, 0);

                outcome.Kind.ShouldBe(RewardKind.Ignored);
            }

            [Fact]
            public void Ignores_no_trader()
            {
                var rewards = new TraderRewards();

                var outcome = rewards.Local(null, 3, 10);

                outcome.Kind.ShouldBe(RewardKind.Ignored);
            }

            [Fact]
            public void Remembers_nothing_it_ignored()
            {
                var rewards = new TraderRewards();
                rewards.Local("Trader", 3, 0);

                var outcome = rewards.Local("Trader", 3, 10);

                outcome.Kind.ShouldBe(RewardKind.Sent);
            }

            [Fact]
            public void Remembers_the_first_after_taking_one_back()
            {
                var rewards = GotTenOnDayThree();
                rewards.Local("Trader", 3, 12);

                var outcome = rewards.Local("Trader", 3, 14);

                outcome.Had.ShouldBe(10);
            }
        }

        public class TakeOnHost
        {
            [Fact]
            public void Gives_the_first_reward_of_the_day()
            {
                var rewards = new TraderRewards();

                var outcome = rewards.TakeOnHost("Trader", 3, 10);

                outcome.ShouldBe(new RewardOutcome
                {
                    Kind = RewardKind.Given,
                    Change = 10,
                });
            }

            [Fact]
            public void Refuses_a_second_one_that_day()
            {
                var rewards = GotTenOnDayThree();

                var outcome = rewards.TakeOnHost("Trader", 3, 12);

                outcome.ShouldBe(new RewardOutcome
                {
                    Kind = RewardKind.Refused,
                    Had = 10,
                });
            }

            [Fact]
            public void Gives_the_next_days_reward()
            {
                var rewards = GotTenOnDayThree();

                var outcome = rewards.TakeOnHost("Trader", 4, 12);

                outcome.Change.ShouldBe(12);
            }

            [Fact]
            public void Remembers_the_one_it_gave()
            {
                var rewards = new TraderRewards();
                rewards.TakeOnHost("Trader", 3, 10);

                var outcome = rewards.TakeOnHost("Trader", 3, 12);

                outcome.Kind.ShouldBe(RewardKind.Refused);
            }
        }

        public class TakeOnClient
        {
            [Fact]
            public void Gives_the_hosts_reward()
            {
                var rewards = new TraderRewards();

                var outcome = rewards.TakeOnClient("Trader", 3, 10);

                outcome.ShouldBe(new RewardOutcome
                {
                    Kind = RewardKind.Given,
                    Change = 10,
                });
            }

            [Fact]
            public void Replaces_our_own_of_the_same_day()
            {
                var rewards = GotTenOnDayThree();

                var outcome = rewards.TakeOnClient("Trader", 3, 14);

                outcome.ShouldBe(new RewardOutcome
                {
                    Kind = RewardKind.Given,
                    Change = 4,
                    Had = 10,
                });
            }

            [Fact]
            public void Gives_it_whole_over_another_days()
            {
                var rewards = GotTenOnDayThree();

                var outcome = rewards.TakeOnClient("Trader", 4, 14);

                outcome.Change.ShouldBe(14);
            }

            [Fact]
            public void Makes_ours_of_the_day_count_as_taken_back()
            {
                var rewards = new TraderRewards();
                rewards.TakeOnClient("Trader", 3, 10);

                var outcome = rewards.Local("Trader", 3, 12);

                outcome.Kind.ShouldBe(RewardKind.TakenBack);
            }
        }

        public class Clear
        {
            [Fact]
            public void Forgets_every_reward()
            {
                var rewards = GotTenOnDayThree();

                rewards.Clear();

                rewards.Local("Trader", 3, 10).Kind.ShouldBe(RewardKind.Sent);
            }
        }
    }
}
