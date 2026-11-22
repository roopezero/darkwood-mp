using DarkwoodMP.World;
using DarkwoodMP.World.Building;

namespace DarkwoodMP.Tests.World.Building
{
    public class ConstructionRulesTests
    {
        public class Limits
        {
            [Fact]
            public void Pins_the_reach_and_no_option()
            {
                ConstructionRules.Reach.ShouldBe(600f);
                ConstructionRules.NoOption.ShouldBe(-1);
            }
        }

        public class Start
        {
            /// A player's own build while sharing, on the host.
            static readonly StartFacts Host = new StartFacts
            {
                Sharing = true,
                Manual = true,
            };

            static readonly StartFacts Client = Host with { Client = true };

            [Fact]
            public void Builds_and_sends_on_the_host()
            {
                var start = ConstructionRules.Start(Host);

                start.ShouldBe(BuildStart.Host);
            }

            [Fact]
            public void Asks_the_host_on_a_client()
            {
                var start = ConstructionRules.Start(Client);

                start.ShouldBe(BuildStart.Client);
            }

            [Fact]
            public void Swallows_a_second_build_while_waiting()
            {
                var build = Client with { Waiting = true };

                var start = ConstructionRules.Start(build);

                start.ShouldBe(BuildStart.Swallow);
            }

            [Fact]
            public void Shares_a_test_steps_build_as_the_players()
            {
                var build = Client with { Manual = false, TestAsPlayer = true };

                var start = ConstructionRules.Start(build);

                start.ShouldBe(BuildStart.Client);
            }

            [Fact]
            public void Leaves_the_games_own_build_alone()
            {
                var build = Client with { Manual = false, Waiting = true };

                var start = ConstructionRules.Start(build);

                start.ShouldBe(BuildStart.Game);
            }

            [Fact]
            public void Leaves_the_joiners_confirmed_copy_alone()
            {
                var build = Client with { Confirming = true, Waiting = true };

                var start = ConstructionRules.Start(build);

                start.ShouldBe(BuildStart.Game);
            }

            [Fact]
            public void Leaves_a_build_while_not_sharing_alone()
            {
                var build = Client with { Sharing = false };

                var start = ConstructionRules.Start(build);

                start.ShouldBe(BuildStart.Game);
            }
        }

        public class ReadsMenu
        {
            [Theory]
            [InlineData(-1, true, true)]
            [InlineData(-1, false, false)]
            [InlineData(0, true, false)]
            public void Reads_the_menu_for_a_players_build_with_no_option(
                int forceOption,
                bool manual,
                bool reads)
            {
                var read = ConstructionRules.ReadsMenu(forceOption, manual);

                read.ShouldBe(reads);
            }
        }

        public class OptionOf
        {
            [Theory]
            [InlineData(-1, 2, 2)]
            [InlineData(1, 2, 1)]
            [InlineData(-2, 2, -2)]
            public void Takes_the_forced_option_or_the_chosen_one(
                int forceOption,
                int chosen,
                int option)
            {
                var built = ConstructionRules.OptionOf(forceOption, chosen);

                built.ShouldBe(option);
            }
        }

        public class HasOption
        {
            [Theory]
            [InlineData(0, 2, true)]
            [InlineData(1, 2, true)]
            [InlineData(2, 2, false)]
            [InlineData(-1, 2, false)]
            public void Has_an_option_from_0_to_below_the_count(
                int option,
                int options,
                bool has)
            {
                var found = ConstructionRules.HasOption(option, options);

                found.ShouldBe(has);
            }
        }

        public class Asks
        {
            [Theory]
            [InlineData(1, 2, true, true)]
            [InlineData(2, 2, true, false)]
            [InlineData(1, 2, false, false)]
            public void Asks_for_an_option_it_has_and_can_name(
                int option,
                int options,
                bool named,
                bool asks)
            {
                var asked = ConstructionRules.Asks(option, options, named);

                asked.ShouldBe(asks);
            }
        }

        public class MayBuild
        {
            /// A client builds option 1 of 2 at a site, exactly at the
            /// reach.
            static readonly BuildFacts Site = new BuildFacts
            {
                Found = true,
                Option = 1,
                Options = 2,
                Name = "barricade",
                Reach = new ReachFacts
                {
                    Handling = true,
                    Known = true,
                    StandIn = new Vec2(0f, 0f),
                    Target = new Vec2(600f, 0f),
                },
            };

            static BuildFacts At(float x)
            {
                return Site with
                {
                    Reach = Site.Reach with { Target = new Vec2(x, 0f) },
                };
            }

            [Fact]
            public void Allows_a_build_exactly_at_the_reach()
            {
                var verdict = ConstructionRules.MayBuild(Site);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_build_just_past_the_reach()
            {
                var verdict = ConstructionRules.MayBuild(At(600.5f));

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Words_the_reach_refusal_with_the_sites_name()
            {
                var verdict = ConstructionRules.MayBuild(At(4041f));

                var text = Refusals.Text(verdict);

                text.ShouldBe("barricade is 4041 away from them (reach 600)");
            }

            [Fact]
            public void Refuses_a_site_that_isnt_here()
            {
                var build = Site with { Found = false };

                var verdict = ConstructionRules.MayBuild(build);

                verdict.Reason.ShouldBe(Refusal.NothingToBuild);
            }

            [Theory]
            [InlineData(2)]
            [InlineData(-1)]
            public void Refuses_an_option_it_doesnt_have(int option)
            {
                var build = Site with { Option = option };

                var verdict = ConstructionRules.MayBuild(build);

                verdict.ShouldBe(
                    Verdict.Refuse(Refusal.NoSuchOption, option.ToString()));
            }

            [Fact]
            public void Refuses_a_repair_of_what_isnt_broken()
            {
                var build = Site with { OnlyWhenDestroyed = true };

                var verdict = ConstructionRules.MayBuild(build);

                verdict.Reason.ShouldBe(Refusal.NotBroken);
            }

            [Fact]
            public void Refuses_what_is_built_already()
            {
                var build = Site with { Constructed = true };

                var verdict = ConstructionRules.MayBuild(build);

                verdict.Reason.ShouldBe(Refusal.BuiltAlready);
            }

            [Fact]
            public void Allows_a_broken_site_built_and_only_for_repairs()
            {
                var build = Site with
                {
                    OnlyWhenDestroyed = true,
                    Constructed = true,
                    Broken = true,
                };

                var verdict = ConstructionRules.MayBuild(build);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_missing_site_before_its_option()
            {
                var build = Site with { Found = false, Option = 5 };

                var verdict = ConstructionRules.MayBuild(build);

                verdict.Reason.ShouldBe(Refusal.NothingToBuild);
            }

            [Fact]
            public void Refuses_the_option_before_the_state()
            {
                var build = Site with { Option = 5, Constructed = true };

                var verdict = ConstructionRules.MayBuild(build);

                verdict.Reason.ShouldBe(Refusal.NoSuchOption);
            }

            [Fact]
            public void Refuses_a_repair_before_built_already()
            {
                var build = Site with
                {
                    OnlyWhenDestroyed = true,
                    Constructed = true,
                };

                var verdict = ConstructionRules.MayBuild(build);

                verdict.Reason.ShouldBe(Refusal.NotBroken);
            }

            [Fact]
            public void Refuses_the_state_before_the_reach()
            {
                var build = At(4041f) with { Constructed = true };

                var verdict = ConstructionRules.MayBuild(build);

                verdict.Reason.ShouldBe(Refusal.BuiltAlready);
            }
        }

        public class Builds
        {
            [Theory]
            [InlineData(true, 1, 2, true)]
            [InlineData(true, 2, 2, false)]
            [InlineData(false, 1, 2, false)]
            public void Builds_a_site_here_with_that_option(
                bool found,
                int option,
                int options,
                bool builds)
            {
                var built = ConstructionRules.Builds(found, option, options);

                built.ShouldBe(builds);
            }
        }
    }
}
