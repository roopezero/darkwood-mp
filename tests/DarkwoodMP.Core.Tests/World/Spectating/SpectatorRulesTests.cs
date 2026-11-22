using DarkwoodMP.World.Spectating;

namespace DarkwoodMP.Tests.World.Spectating
{
    public class SpectatorRulesTests
    {
        public class Limits
        {
            [Fact]
            public void Pins_the_spectators_limits()
            {
                var limits = new[]
                {
                    SpectatorRules.DeadWait,
                    SpectatorRules.NobodyFor,
                    Roster.Grace,
                };

                limits.ShouldBe(new[] { 20f, 3f, 10f });
            }
        }

        public class MayBegin
        {
            static readonly BeginFacts Watching = new BeginFacts
            {
                InSession = true,
                InWorld = true,
                Playing = 1,
            };

            [Fact]
            public void Watches_someone_still_playing()
            {
                SpectatorRules.MayBegin(Watching).ShouldBeTrue();
            }

            [Fact]
            public void Lets_the_permadeath_through_with_nobody_playing()
            {
                var facts = Watching with { Playing = 0 };

                SpectatorRules.MayBegin(facts).ShouldBeFalse();
            }

            [Fact]
            public void Lets_the_permadeath_through_once_allowed()
            {
                var facts = Watching with { AllowPermadeath = true };

                SpectatorRules.MayBegin(facts).ShouldBeFalse();
            }

            [Theory]
            [InlineData(false, true)]
            [InlineData(true, false)]
            public void Needs_a_session_and_a_world(bool session, bool world)
            {
                var facts = Watching with
                {
                    InSession = session,
                    InWorld = world,
                };

                SpectatorRules.MayBegin(facts).ShouldBeFalse();
            }
        }

        public class DeadWaits
        {
            static readonly DeadWaitFacts Waiting = new DeadWaitFacts
            {
                Now = new Seconds(119.9f),
                Until = new Seconds(120f),
                InWorld = true,
            };

            [Fact]
            public void Waits_just_before_the_deadline()
            {
                SpectatorRules.DeadWaits(Waiting).ShouldBe(DeadWaitStep.Waits);
            }

            [Fact]
            public void Gives_up_exactly_at_the_deadline_whatever_else()
            {
                var facts = Waiting with
                {
                    Now = new Seconds(120f),
                    Playing = 1,
                };

                SpectatorRules.DeadWaits(facts)
                    .ShouldBe(DeadWaitStep.Permadeath);
            }

            [Fact]
            public void Begins_once_someone_plays()
            {
                var facts = Waiting with { Playing = 1 };

                SpectatorRules.DeadWaits(facts).ShouldBe(DeadWaitStep.Begins);
            }

            [Theory]
            [InlineData(false, false)]
            [InlineData(true, true)]
            public void Stops_out_of_a_world_or_once_watching(
                bool world,
                bool active)
            {
                var facts = Waiting with
                {
                    InWorld = world,
                    Active = active,
                    Playing = 1,
                };

                SpectatorRules.DeadWaits(facts).ShouldBe(DeadWaitStep.Stops);
            }
        }

        public class MayWatch
        {
            [Theory]
            [InlineData(false, true, 1, true)]
            [InlineData(true, true, 1, false)]
            [InlineData(false, false, 1, false)]
            [InlineData(false, true, 0, false)]
            public void Watches_in_the_epilogue_with_someone_crawling(
                bool active,
                bool session,
                int playing,
                bool watches)
            {
                SpectatorRules.MayWatch(active, session, playing)
                    .ShouldBe(watches);
            }
        }

        public class Watch
        {
            /// Nobody to watch since 100 s, exactly NobodyFor later.
            static readonly WatchFacts Nobody = new WatchFacts
            {
                InSession = true,
                InWorld = true,
                Nobody = true,
                NobodySince = new Seconds(100f),
                Now = new Seconds(103f),
            };

            [Fact]
            public void Watches_someone_playing_and_forgets_the_wait()
            {
                var facts = Nobody with { Playing = 2 };

                var step = SpectatorRules.Watch(facts);

                step.Action.ShouldBe(WatchAction.Watch);
                step.Nobody.ShouldBeFalse();
            }

            [Fact]
            public void Waits_exactly_at_the_limit()
            {
                SpectatorRules.Watch(Nobody).Action
                    .ShouldBe(WatchAction.Waits);
            }

            [Fact]
            public void Ends_just_past_the_limit()
            {
                var facts = Nobody with { Now = new Seconds(103.1f) };

                SpectatorRules.Watch(facts).Action
                    .ShouldBe(WatchAction.NobodyLeft);
            }

            [Fact]
            public void Starts_the_wait_on_the_first_frame_with_nobody()
            {
                var facts = Nobody with { Nobody = false };

                var step = SpectatorRules.Watch(facts);

                step.Action.ShouldBe(WatchAction.Waits);
                step.Nobody.ShouldBeTrue();
                step.NobodySince.Value.ShouldBe(103f);
            }

            [Fact]
            public void Stays_in_the_epilogue_with_nobody()
            {
                var facts = Nobody with
                {
                    Epilogue = true,
                    InSession = false,
                    InWorld = false,
                    Now = new Seconds(900f),
                };

                SpectatorRules.Watch(facts).Action
                    .ShouldBe(WatchAction.Stay);
            }

            [Fact]
            public void Ends_out_of_a_world_before_the_session_counts()
            {
                var facts = Nobody with
                {
                    InSession = false,
                    InWorld = false,
                };

                SpectatorRules.Watch(facts).Action
                    .ShouldBe(WatchAction.Ended);
            }

            [Fact]
            public void Counts_nobody_playing_out_of_the_session()
            {
                var facts = Nobody with { InSession = false, Playing = 2 };

                SpectatorRules.Watch(facts).Action
                    .ShouldBe(WatchAction.SessionLost);
            }
        }

        public class Pick
        {
            [Theory]
            [InlineData(1, 3, Choice.None, 1)]
            [InlineData(-1, 3, Choice.None, 0)]
            [InlineData(1, 3, Choice.Next, 2)]
            [InlineData(2, 3, Choice.Next, 0)]
            [InlineData(1, 3, Choice.Previous, 0)]
            [InlineData(0, 3, Choice.Previous, 2)]
            [InlineData(-1, 3, Choice.Previous, 2)]
            [InlineData(0, 1, Choice.Next, 0)]
            public void Moves_round_the_players(
                int found,
                int count,
                Choice choice,
                int index)
            {
                SpectatorRules.Pick(found, count, choice).ShouldBe(index);
            }
        }
    }
}
