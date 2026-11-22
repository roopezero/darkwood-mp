using DarkwoodMP.World.Mornings;

namespace DarkwoodMP.Tests.World.Mornings
{
    public partial class MorningRulesTests
    {
        public class OnLeft
        {
            [Fact]
            public void Ends_the_hosts_own_morning()
            {
                MorningRules.OnLeft(Left).ShouldBe(LeftStep.End);
            }

            [Fact]
            public void Waits_for_the_hosts_dialogue_first()
            {
                var left = Left with { Talking = true };

                MorningRules.OnLeft(left).ShouldBe(LeftStep.EndAfterTalk);
            }

            [Fact]
            public void Relays_with_no_morning_of_its_own()
            {
                var left = Left with { InMorning = false, Talking = true };

                MorningRules.OnLeft(left).ShouldBe(LeftStep.Relay);
            }

            [Fact]
            public void Relays_while_the_host_dreams()
            {
                var left = Left with { Dreaming = true };

                MorningRules.OnLeft(left).ShouldBe(LeftStep.Relay);
            }

            [Fact]
            public void Ignores_a_sender_not_connected()
            {
                var left = Left with { FromPeer = false };

                MorningRules.OnLeft(left).ShouldBe(LeftStep.Ignore);
            }

            [Fact]
            public void Ignores_it_on_a_client()
            {
                var left = Left with { IsHost = false };

                MorningRules.OnLeft(left).ShouldBe(LeftStep.Ignore);
            }

            [Fact]
            public void Ignores_it_out_of_a_world()
            {
                var left = Left with { InWorld = false };

                MorningRules.OnLeft(left).ShouldBe(LeftStep.Ignore);
            }

            [Fact]
            public void Ignores_it_without_a_controller()
            {
                var left = Left with { HasController = false };

                MorningRules.OnLeft(left).ShouldBe(LeftStep.Ignore);
            }
        }

        public class OnEnded
        {
            [Fact]
            public void Ends_our_morning()
            {
                MorningRules.OnEnded(Ended).ShouldBe(EndedStep.End);
            }

            [Fact]
            public void Waits_for_our_dialogue_first()
            {
                var ended = Ended with { Talking = true };

                MorningRules.OnEnded(ended).ShouldBe(EndedStep.EndAfterTalk);
            }

            [Fact]
            public void Ignores_one_while_an_end_is_waiting()
            {
                var ended = Ended with { Pending = true, InMorning = false };

                MorningRules.OnEnded(ended).ShouldBe(EndedStep.Ignore);
            }

            [Fact]
            public void Only_logs_when_ours_was_over()
            {
                var ended = Ended with { InMorning = false, Talking = true };

                MorningRules.OnEnded(ended).ShouldBe(EndedStep.AlreadyOver);
            }

            [Fact]
            public void Only_logs_while_dreaming()
            {
                var ended = Ended with { Dreaming = true };

                MorningRules.OnEnded(ended).ShouldBe(EndedStep.AlreadyOver);
            }

            [Fact]
            public void Only_logs_without_a_controller()
            {
                var ended = Ended with { HasController = false };

                MorningRules.OnEnded(ended).ShouldBe(EndedStep.AlreadyOver);
            }

            [Fact]
            public void Ignores_one_not_from_the_host()
            {
                var ended = Ended with { FromHost = false };

                MorningRules.OnEnded(ended).ShouldBe(EndedStep.Ignore);
            }

            [Fact]
            public void Ignores_it_on_the_host()
            {
                var ended = Ended with { IsClient = false };

                MorningRules.OnEnded(ended).ShouldBe(EndedStep.Ignore);
            }

            [Fact]
            public void Ignores_it_out_of_a_world()
            {
                var ended = Ended with { InWorld = false };

                MorningRules.OnEnded(ended).ShouldBe(EndedStep.Ignore);
            }
        }

        public class EndsAsReplay
        {
            [Theory]
            [InlineData(true, false)]
            [InlineData(false, true)]
            public void Replays_only_on_a_client(bool host, bool replay)
            {
                MorningRules.EndsAsReplay(host).ShouldBe(replay);
            }
        }

        public class Talking
        {
            [Fact]
            public void Finds_a_dialogue_with_the_visitor()
            {
                MorningRules.Talking(Visitor).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_out_another_npc()
            {
                var visitor = Visitor with { AtBase = false };

                MorningRules.Talking(visitor).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_window_with_no_npc()
            {
                var visitor = Visitor with { HasNpc = false };

                MorningRules.Talking(visitor).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_player_not_in_dialogue()
            {
                var visitor = Visitor with { InDialogue = false };

                MorningRules.Talking(visitor).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_game_with_no_locations()
            {
                var visitor = Visitor with { HasLocations = false };

                MorningRules.Talking(visitor).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_game_with_no_window()
            {
                var visitor = Visitor with { HasWindow = false };

                MorningRules.Talking(visitor).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_game_with_no_player()
            {
                var visitor = Visitor with { HasPlayer = false };

                MorningRules.Talking(visitor).ShouldBeFalse();
            }
        }

        public class Pending
        {
            [Fact]
            public void Closes_the_dialogue_while_we_talk()
            {
                MorningRules.Pending(Waiting).ShouldBe(PendingStep.Close);
            }

            [Fact]
            public void Ends_once_we_stopped_talking()
            {
                var pending = Waiting with { Talking = false };

                MorningRules.Pending(pending).ShouldBe(PendingStep.End);
            }

            [Fact]
            public void Does_nothing_with_no_end_waiting()
            {
                var pending = Waiting with { Pending = false };

                MorningRules.Pending(pending).ShouldBe(PendingStep.Nothing);
            }

            [Fact]
            public void Forgets_it_out_of_a_world()
            {
                var pending = Waiting with { InWorld = false };

                MorningRules.Pending(pending).ShouldBe(PendingStep.Forget);
            }

            [Fact]
            public void Forgets_it_without_a_window()
            {
                var pending = Waiting with { HasWindow = false };

                MorningRules.Pending(pending).ShouldBe(PendingStep.Forget);
            }

            [Fact]
            public void Forgets_it_without_a_player()
            {
                var pending = Waiting with { HasPlayer = false };

                MorningRules.Pending(pending).ShouldBe(PendingStep.Forget);
            }

            [Fact]
            public void Forgets_it_without_a_controller()
            {
                var pending = Waiting with { HasController = false };

                MorningRules.Pending(pending).ShouldBe(PendingStep.Forget);
            }
        }

        public class AfterClose
        {
            static readonly Seconds Began = new Seconds(100f);

            [Fact]
            public void Ends_once_the_npc_let_go()
            {
                var now = new Seconds(200f);

                var step = MorningRules.AfterClose(true, now, Began);

                step.ShouldBe(CloseStep.End);
            }

            [Fact]
            public void Waits_exactly_at_the_timeout()
            {
                var now = new Seconds(110f);

                var step = MorningRules.AfterClose(false, now, Began);

                step.ShouldBe(CloseStep.Wait);
            }

            [Fact]
            public void Forces_it_just_past_the_timeout()
            {
                var now = new Seconds(110.01f);

                var step = MorningRules.AfterClose(false, now, Began);

                step.ShouldBe(CloseStep.ForceEnd);
            }
        }
    }
}
