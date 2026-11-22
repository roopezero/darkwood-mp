using DarkwoodMP.World.Scripted;

namespace DarkwoodMP.Tests.World.Scripted
{
    public class FiringRulesTests
    {
        /// Our player's trigger, the first of its object, while sharing.
        static readonly FiringFacts Ours = new FiringFacts
        {
            Firing = true,
            HasTriggers = true,
            Sharing = true,
        };

        public class Share
        {
            [Fact]
            public void Sends_our_players_trigger()
            {
                FiringRules.Share(Ours).ShouldBe(FiringShare.Send);
            }

            [Fact]
            public void Sends_a_world_trigger_from_the_host()
            {
                var f = Ours with { World = true };

                FiringRules.Share(f).ShouldBe(FiringShare.Send);
            }

            [Fact]
            public void Sends_a_world_trigger_a_client_caused_as_theirs()
            {
                var f = Ours with { World = true, By = 42 };

                FiringRules.Share(f).ShouldBe(FiringShare.Caused);
            }

            [Fact]
            public void Sends_a_players_trigger_whatever_the_cause()
            {
                var f = Ours with { By = 42 };

                FiringRules.Share(f).ShouldBe(FiringShare.Send);
            }

            [Fact]
            public void Leaves_a_clients_world_trigger_to_the_host()
            {
                var f = Ours with { World = true, Client = true, By = 42 };

                FiringRules.Share(f).ShouldBe(FiringShare.Game);
            }

            [Fact]
            public void Sends_a_clients_own_trigger()
            {
                var f = Ours with { Client = true };

                FiringRules.Share(f).ShouldBe(FiringShare.Send);
            }

            [Fact]
            public void Runs_events_that_ran_once_as_the_game_does()
            {
                var f = Ours with { Fired = true };

                FiringRules.Share(f).ShouldBe(FiringShare.Game);
            }

            [Fact]
            public void Sends_repeatable_events_that_ran()
            {
                var f = Ours with { Fired = true, MultipleFire = true };

                FiringRules.Share(f).ShouldBe(FiringShare.Send);
            }

            [Fact]
            public void Runs_them_as_the_game_does_when_not_sharing()
            {
                var f = Ours with { Sharing = false };

                FiringRules.Share(f).ShouldBe(FiringShare.Game);
            }

            [Fact]
            public void Passes_when_no_trigger_is_firing()
            {
                var f = Ours with { Firing = false };

                FiringRules.Share(f).ShouldBe(FiringShare.Pass);
            }

            [Fact]
            public void Passes_a_trigger_with_no_index()
            {
                var f = Ours with { Index = -1 };

                FiringRules.Share(f).ShouldBe(FiringShare.Pass);
            }

            [Fact]
            public void Passes_a_trigger_with_no_triggers_object()
            {
                var f = Ours with { HasTriggers = false };

                FiringRules.Share(f).ShouldBe(FiringShare.Pass);
            }

            [Fact]
            public void Passes_inside_a_part_being_run()
            {
                var f = Ours with { Current = EventPart.SharedOnly };

                FiringRules.Share(f).ShouldBe(FiringShare.Pass);
            }

            [Fact]
            public void Passes_before_looking_at_the_events()
            {
                var f = Ours with
                {
                    Current = EventPart.SharedOnly,
                    Fired = true,
                };

                FiringRules.Share(f).ShouldBe(FiringShare.Pass);
            }
        }

        public class RunsAtOnce
        {
            [Theory]
            [InlineData(false, false, true)]
            [InlineData(false, true, true)]
            [InlineData(true, false, true)]
            [InlineData(true, true, false)]
            public void Waits_only_for_a_clients_sent_trigger(
                bool client,
                bool sent,
                bool runs)
            {
                FiringRules.RunsAtOnce(client, sent).ShouldBe(runs);
            }
        }

        public class LeftBeforeYes
        {
            [Theory]
            [InlineData(true, 2, 3, true)]
            [InlineData(true, 2, 2, false)]
            [InlineData(false, 2, 3, false)]
            public void Notices_a_repeatable_area_left_before_the_yes(
                bool repeatableArea,
                int before,
                int exited,
                bool left)
            {
                var result =
                    FiringRules.LeftBeforeYes(repeatableArea, before, exited);

                result.ShouldBe(left);
            }
        }
    }
}
