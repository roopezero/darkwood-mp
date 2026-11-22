using DarkwoodMP.World.Rolls;

namespace DarkwoodMP.Tests.World.Rolls
{
    public class RollRulesTests
    {
        public class Limits
        {
            [Fact]
            public void Pins_the_send_times()
            {
                var limits = new[]
                {
                    RollRules.CheckEvery,
                    RollRules.Resend,
                    RollRules.ObjectsAfter,
                };

                limits.ShouldBe(new[] { 1f, 10f, 2f });
            }
        }

        public class Follows
        {
            [Theory]
            [InlineData(true, false, false, true)]
            [InlineData(true, true, false, false)]
            [InlineData(true, false, true, false)]
            [InlineData(false, false, false, false)]
            [InlineData(false, true, true, false)]
            public void Follows_on_a_client_in_the_shared_world(
                bool client,
                bool outside,
                bool dreaming,
                bool follows)
            {
                RollRules.Follows(client, outside, dreaming)
                    .ShouldBe(follows);
            }
        }

        public class FromHost
        {
            [Theory]
            [InlineData(1UL, 1UL, true, true)]
            [InlineData(2UL, 1UL, true, false)]
            [InlineData(1UL, 1UL, false, false)]
            public void Takes_the_hosts_message_in_a_world(
                ulong from,
                ulong host,
                bool inWorld,
                bool takes)
            {
                RollRules.FromHost(from, host, inWorld).ShouldBe(takes);
            }
        }

        public class RunsRandomEvent
        {
            [Theory]
            [InlineData(false, false, false, true)]
            [InlineData(true, false, true, false)]
            [InlineData(true, true, false, false)]
            [InlineData(true, true, true, true)]
            [InlineData(false, true, false, true)]
            public void Runs_only_a_presented_location_event_when_following(
                bool follows,
                bool presenting,
                bool locationEvent,
                bool runs)
            {
                RollRules.RunsRandomEvent(follows, presenting, locationEvent)
                    .ShouldBe(runs);
            }
        }

        public class Presents
        {
            [Theory]
            [InlineData(NightStep.Sound)]
            [InlineData(NightStep.ChangeIdleSound)]
            [InlineData(NightStep.PlaySoundUnused)]
            [InlineData(NightStep.Particle)]
            [InlineData(NightStep.CameraEffect)]
            [InlineData(NightStep.TweenColor)]
            [InlineData(NightStep.PlayAnim)]
            [InlineData(NightStep.DisplayMessage)]
            [InlineData(NightStep.Debug)]
            public void Presents_what_is_seen_or_heard(NightStep step)
            {
                RollRules.Presents(step, true).ShouldBeTrue();
            }

            [Fact]
            public void Presents_a_fire_event_of_game_events()
            {
                RollRules.Presents(NightStep.FireEvent, false)
                    .ShouldBeTrue();
            }

            [Fact]
            public void Drops_a_fire_event_of_a_world_event()
            {
                RollRules.Presents(NightStep.FireEvent, true)
                    .ShouldBeFalse();
            }

            [Fact]
            public void Drops_any_other_step()
            {
                RollRules.Presents(NightStep.Other, false).ShouldBeFalse();
            }
        }

        public class PlayStep
        {
            [Theory]
            [InlineData(false, true, true)]
            [InlineData(true, false, true)]
            [InlineData(false, false, false)]
            public void Leaves_a_step_outside_a_followed_night_event(
                bool presenting,
                bool follows,
                bool presents)
            {
                RollRules.PlayStep(presenting, follows, presents)
                    .ShouldBe(StepPlay.Untouched);
            }

            [Fact]
            public void Presents_a_step_that_is_seen()
            {
                RollRules.PlayStep(true, true, true)
                    .ShouldBe(StepPlay.Presented);
            }

            [Fact]
            public void Drops_a_step_that_changes_the_world()
            {
                RollRules.PlayStep(true, true, false)
                    .ShouldBe(StepPlay.Dropped);
            }
        }

        public class ShadowLights
        {
            [Theory]
            [InlineData(true, false, ShadowTurn.Skip)]
            [InlineData(true, true, ShadowTurn.Skip)]
            [InlineData(false, true, ShadowTurn.HostRolls)]
            [InlineData(false, false, ShadowTurn.Game)]
            public void Lets_the_host_roll_and_a_joiner_wait(
                bool follows,
                bool hostSharing,
                ShadowTurn turn)
            {
                RollRules.ShadowLights(follows, hostSharing).ShouldBe(turn);
            }
        }

        public class ObjectId
        {
            [Fact]
            public void Puts_the_first_roll_above_bit_31()
            {
                RollRules.ObjectId(1, 0).ShouldBe(1L << 31);
            }

            [Fact]
            public void Puts_the_second_roll_below()
            {
                RollRules.ObjectId(0, 5).ShouldBe(5L);
            }

            [Fact]
            public void Joins_the_largest_rolls()
            {
                var id = RollRules.ObjectId(int.MaxValue, int.MaxValue);

                id.ShouldBe(0x3FFFFFFFFFFFFFFFL);
            }
        }

        public class InRange
        {
            [Theory]
            [InlineData(0, 1, true)]
            [InlineData(4, 5, true)]
            [InlineData(5, 5, false)]
            [InlineData(0, 0, false)]
            public void Takes_an_index_below_the_count(
                int index,
                int count,
                bool fits)
            {
                RollRules.InRange(index, count).ShouldBe(fits);
            }
        }

        public class TakesNightObject
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(true, true, false)]
            [InlineData(false, false, false)]
            public void Makes_a_mushroom_it_hasnt_on_a_client(
                bool client,
                bool known,
                bool takes)
            {
                RollRules.TakesNightObject(client, known).ShouldBe(takes);
            }
        }

        public class SendsNightObject
        {
            [Theory]
            [InlineData(2, 3, true, true)]
            [InlineData(3, 3, true, false)]
            [InlineData(2, 3, false, false)]
            [InlineData(3, 2, true, false)]
            public void Sends_a_new_mushroom_at_a_location_with_one(
                int before,
                int after,
                bool mushroom,
                bool sends)
            {
                RollRules.SendsNightObject(before, after, mushroom)
                    .ShouldBe(sends);
            }
        }
    }
}
