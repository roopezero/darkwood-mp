using DarkwoodMP.World.Scripted;

namespace DarkwoodMP.Tests.World.Scripted
{
    public class WorldEventRulesTests
    {
        public class MaySend
        {
            /// A known world event that can happen, while sharing.
            static readonly WorldEventFacts Event = new WorldEventFacts
            {
                Sharing = true,
                Known = true,
            };

            [Fact]
            public void Considers_a_world_event_that_can_happen()
            {
                WorldEventRules.MaySend(Event).ShouldBeTrue();
            }

            [Fact]
            public void Considers_a_repeatable_one_that_happened()
            {
                var f = Event with { Fired = true, MultipleFire = true };

                WorldEventRules.MaySend(f).ShouldBeTrue();
            }

            [Fact]
            public void Skips_one_inside_a_part_being_run()
            {
                var f = Event with { Current = EventPart.HostAndShared };

                WorldEventRules.MaySend(f).ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_replay()
            {
                var f = Event with { Replaying = true };

                WorldEventRules.MaySend(f).ShouldBeFalse();
            }

            [Fact]
            public void Skips_it_when_not_sharing()
            {
                var f = Event with { Sharing = false };

                WorldEventRules.MaySend(f).ShouldBeFalse();
            }

            [Fact]
            public void Skips_one_the_host_sent_as_an_animations()
            {
                var f = Event with { AnimationSent = true };

                WorldEventRules.MaySend(f).ShouldBeFalse();
            }

            [Fact]
            public void Skips_the_epilogues_pan()
            {
                var f = Event with { Pan = true };

                WorldEventRules.MaySend(f).ShouldBeFalse();
            }

            [Fact]
            public void Skips_an_unknown_one()
            {
                var f = Event with { Known = false };

                WorldEventRules.MaySend(f).ShouldBeFalse();
            }

            [Fact]
            public void Skips_one_that_happened()
            {
                var f = Event with { Fired = true };

                WorldEventRules.MaySend(f).ShouldBeFalse();
            }
        }

        public class Sends
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(true, true, false)]
            [InlineData(false, false, false)]
            public void Sends_only_ours_outside_our_own_area(
                bool ours,
                bool ownArea,
                bool sends)
            {
                WorldEventRules.Sends(ours, ownArea).ShouldBe(sends);
            }
        }

        public class FromAnimation
        {
            /// The host with a peer, a character firing a known event.
            static readonly AnimationFacts Host = new AnimationFacts
            {
                Active = true,
                InWorld = true,
                Character = true,
                Known = true,
                Peers = 1,
            };

            static readonly AnimationFacts Client = Host with
            {
                Client = true,
            };

            [Fact]
            public void Sends_it_from_the_host()
            {
                var outcome = WorldEventRules.FromAnimation(Host);

                outcome.ShouldBe(AnimationEvent.SendAndRun);
            }

            [Fact]
            public void Runs_it_on_a_host_with_no_peers()
            {
                var f = Host with { Peers = 0 };

                var outcome = WorldEventRules.FromAnimation(f);

                outcome.ShouldBe(AnimationEvent.Run);
            }

            [Fact]
            public void Runs_one_that_happened_without_sending_it()
            {
                var f = Host with { Fired = true };

                var outcome = WorldEventRules.FromAnimation(f);

                outcome.ShouldBe(AnimationEvent.Run);
            }

            [Fact]
            public void Sends_a_repeatable_one_that_happened()
            {
                var f = Host with { Fired = true, MultipleFire = true };

                var outcome = WorldEventRules.FromAnimation(f);

                outcome.ShouldBe(AnimationEvent.SendAndRun);
            }

            [Fact]
            public void Leaves_it_to_the_host_on_a_client()
            {
                var outcome = WorldEventRules.FromAnimation(Client);

                outcome.ShouldBe(AnimationEvent.LeaveToHost);
            }

            [Fact]
            public void Leaves_a_spent_one_to_the_host_on_a_client()
            {
                var f = Client with { Fired = true, Peers = 0 };

                var outcome = WorldEventRules.FromAnimation(f);

                outcome.ShouldBe(AnimationEvent.LeaveToHost);
            }

            [Fact]
            public void Runs_it_in_a_clients_own_area()
            {
                var f = Client with { LocalOnly = true };

                var outcome = WorldEventRules.FromAnimation(f);

                outcome.ShouldBe(AnimationEvent.Run);
            }

            [Fact]
            public void Runs_a_players_animation_as_it_was()
            {
                var f = Client with { Character = false };

                var outcome = WorldEventRules.FromAnimation(f);

                outcome.ShouldBe(AnimationEvent.Run);
            }

            [Fact]
            public void Runs_an_unknown_event_as_it_was()
            {
                var f = Client with { Known = false };

                var outcome = WorldEventRules.FromAnimation(f);

                outcome.ShouldBe(AnimationEvent.Run);
            }

            [Theory]
            [InlineData(false, true, EventPart.All, false)]
            [InlineData(true, false, EventPart.All, false)]
            [InlineData(true, true, EventPart.SharedOnly, false)]
            [InlineData(true, true, EventPart.All, true)]
            public void Runs_it_as_it_was_when_not_together_or_replaying(
                bool active,
                bool world,
                EventPart current,
                bool replaying)
            {
                var f = Client with
                {
                    Active = active,
                    InWorld = world,
                    Current = current,
                    Replaying = replaying,
                };

                var outcome = WorldEventRules.FromAnimation(f);

                outcome.ShouldBe(AnimationEvent.Run);
            }
        }
    }
}
