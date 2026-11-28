using DarkwoodMP.World.Story;

namespace DarkwoodMP.Tests.World.Story
{
    public partial class EpilogueRulesTests
    {
        public class OnStep
        {
            static readonly EpilogueStepFacts FromJoiner = new EpilogueStepFacts
            {
                Playing = true,
                IsHost = true,
                FromPeer = true,
                Step = EpilogueStep.Crawled,
            };

            static readonly EpilogueStepFacts FromHost = new EpilogueStepFacts
            {
                Playing = true,
                FromHost = true,
                Step = EpilogueStep.Pan,
            };

            [Theory]
            [InlineData(EpilogueStep.Crawled, StepHandling.Crawled)]
            [InlineData(EpilogueStep.Burn, StepHandling.Burned)]
            [InlineData(EpilogueStep.Pan, StepHandling.Ignore)]
            [InlineData(EpilogueStep.LayoutAsk, StepHandling.LayoutAsk)]
            [InlineData(EpilogueStep.Layout, StepHandling.Ignore)]
            [InlineData(EpilogueStep.Outcomes, StepHandling.Ignore)]
            [InlineData(EpilogueStep.None, StepHandling.Ignore)]
            public void Takes_a_peers_crawl_and_fire_on_the_host(
                EpilogueStep step,
                StepHandling handling)
            {
                var facts = FromJoiner with { Step = step };

                EpilogueRules.OnStep(facts).ShouldBe(handling);
            }

            [Fact]
            public void Ignores_a_stranger_on_the_host()
            {
                var facts = FromJoiner with { FromPeer = false };

                EpilogueRules.OnStep(facts).ShouldBe(StepHandling.Ignore);
            }

            [Theory]
            [InlineData(EpilogueStep.Pan, StepHandling.Pan)]
            [InlineData(EpilogueStep.Outcomes, StepHandling.Outcomes)]
            [InlineData(EpilogueStep.Burn, StepHandling.Burned)]
            [InlineData(EpilogueStep.Crawled, StepHandling.Ignore)]
            [InlineData(EpilogueStep.Layout, StepHandling.Layout)]
            [InlineData(EpilogueStep.LayoutAsk, StepHandling.Ignore)]
            [InlineData(EpilogueStep.None, StepHandling.Ignore)]
            public void Takes_the_hosts_pan_outcomes_and_fire_on_a_joiner(
                EpilogueStep step,
                StepHandling handling)
            {
                var facts = FromHost with { Step = step };

                EpilogueRules.OnStep(facts).ShouldBe(handling);
            }

            [Fact]
            public void Ignores_anyone_but_the_host_on_a_joiner()
            {
                var facts = FromHost with { FromHost = false, FromPeer = true };

                EpilogueRules.OnStep(facts).ShouldBe(StepHandling.Ignore);
            }

            [Fact]
            public void Ignores_everything_when_not_playing()
            {
                var facts = FromJoiner with { Playing = false };

                EpilogueRules.OnStep(facts).ShouldBe(StepHandling.Ignore);
            }
        }

        public class SharesBurn
        {
            static readonly BurnShareFacts Fire = new BurnShareFacts
            {
                Playing = true,
                Name = "Istota_lv1_events",
                InSession = true,
            };

            [Fact]
            public void Shares_our_players_fire()
            {
                EpilogueRules.SharesBurn(Fire).ShouldBeTrue();
            }

            [Fact]
            public void Shares_it_once()
            {
                var facts = Fire with { Shared = true };

                EpilogueRules.SharesBurn(facts).ShouldBeFalse();
            }

            [Fact]
            public void Doesnt_share_a_replay()
            {
                var facts = Fire with { Replaying = true };

                EpilogueRules.SharesBurn(facts).ShouldBeFalse();
            }

            [Theory]
            [InlineData("Istota_lv2_events")]
            [InlineData(null)]
            public void Doesnt_share_other_events(string name)
            {
                var facts = Fire with { Name = name };

                EpilogueRules.SharesBurn(facts).ShouldBeFalse();
            }

            [Theory]
            [InlineData(false, true)]
            [InlineData(true, false)]
            public void Needs_the_epilogue_in_a_session(
                bool playing,
                bool session)
            {
                var facts = Fire with
                {
                    Playing = playing,
                    InSession = session,
                };

                EpilogueRules.SharesBurn(facts).ShouldBeFalse();
            }
        }

        public class Burned
        {
            static readonly BurnFacts Found = new BurnFacts { Found = true };

            [Fact]
            public void Runs_the_events_here()
            {
                EpilogueRules.Burned(Found).ShouldBe(BurnStep.Runs);
            }

            [Fact]
            public void Runs_events_that_fire_more_than_once_again()
            {
                var facts = Found with { Fired = true, Multiple = true };

                EpilogueRules.Burned(facts).ShouldBe(BurnStep.Runs);
            }

            [Fact]
            public void Doesnt_run_events_that_fired_once()
            {
                var facts = Found with { Fired = true };

                EpilogueRules.Burned(facts).ShouldBe(BurnStep.AlreadyFired);
            }

            [Fact]
            public void Warns_when_our_part_has_no_such_events()
            {
                var facts = Found with { Found = false };

                EpilogueRules.Burned(facts).ShouldBe(BurnStep.Missing);
            }

            [Fact]
            public void Does_nothing_once_shared_whatever_else()
            {
                var facts = Found with { Shared = true, Found = false };

                EpilogueRules.Burned(facts).ShouldBe(BurnStep.Ignored);
            }
        }
    }
}
