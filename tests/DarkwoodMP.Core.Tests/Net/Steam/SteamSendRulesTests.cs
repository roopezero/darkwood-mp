using DarkwoodMP.Net.Steam;

namespace DarkwoodMP.Tests.Net.Steam
{
    public class SteamSendRulesTests
    {
        /// A peer whose queue emptied and whose close time is set.
        static readonly CloseFacts Armed = new CloseFacts
        {
            Armed = true,
            At = new Seconds(10f),
            Now = new Seconds(10f),
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_sizes()
            {
                var sizes = new long[]
                {
                    SteamSendRules.MaxMessage,
                    SteamSendRules.PieceSize,
                    SteamSendRules.MaxWaiting,
                };

                sizes.ShouldBe(new long[] { 524288, 262144, 67108864 });
            }

            [Fact]
            public void Pins_the_close_delay()
            {
                var delay = SteamSendRules.CloseDelay;

                delay.ShouldBe(1f);
            }
        }

        public class TooLongForOne
        {
            [Theory]
            [InlineData(524288, false)]
            [InlineData(524289, true)]
            public void Splits_past_steams_limit(int length, bool split)
            {
                var result = SteamSendRules.TooLongForOne(length);

                result.ShouldBe(split);
            }
        }

        public class TooLong
        {
            [Fact]
            public void Refuses_with_whole_kib_and_the_type()
            {
                var verdict = SteamSendRules.TooLong(655871, "SaveData");

                verdict.ShouldBe(
                    Verdict.Refuse(
                        Refusal.TooLongForSteam,
                        Refusals.Detail("640", "SaveData")));
            }
        }

        public class WaitsBehind
        {
            [Theory]
            [InlineData(0, false)]
            [InlineData(1, true)]
            public void Waits_behind_a_queue(int queued, bool waits)
            {
                var result = SteamSendRules.WaitsBehind(queued);

                result.ShouldBe(waits);
            }
        }

        public class AfterReliable
        {
            [Theory]
            [InlineData(SendResult.Sent, SendStep.Sent)]
            [InlineData(SendResult.Full, SendStep.Queue)]
            [InlineData(SendResult.Failed, SendStep.Failed)]
            public void Queues_only_what_steam_had_no_room_for(
                SendResult result,
                SendStep step)
            {
                var after = SteamSendRules.AfterReliable(result);

                after.ShouldBe(step);
            }
        }

        public class AfterUnreliable
        {
            [Theory]
            [InlineData(SendResult.Sent, true)]
            [InlineData(SendResult.Full, false)]
            [InlineData(SendResult.Failed, false)]
            public void Drops_whatever_didnt_go(SendResult result, bool sent)
            {
                var after = SteamSendRules.AfterUnreliable(result);

                after.ShouldBe(sent);
            }
        }

        public class MayQueue
        {
            [Fact]
            public void Allows_a_queue_exactly_at_the_limit()
            {
                var verdict = SteamSendRules.MayQueue(67108864 - 1000, 1000);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_queue_just_past_the_limit()
            {
                var verdict = SteamSendRules.MayQueue(67108864 - 1000, 1001);

                verdict.Reason.ShouldBe(Refusal.QueueFull);
            }

            [Fact]
            public void Names_the_whole_kib_already_waiting()
            {
                var verdict = SteamSendRules.MayQueue(67108863, 2);

                verdict.Detail.ShouldBe("65535");
            }
        }

        public class AfterFlush
        {
            [Theory]
            [InlineData(SendResult.Sent, FlushStep.Next)]
            [InlineData(SendResult.Full, FlushStep.Stop)]
            [InlineData(SendResult.Failed, FlushStep.Drop)]
            public void Stops_only_while_steam_is_full(
                SendResult result,
                FlushStep step)
            {
                var after = SteamSendRules.AfterFlush(result);

                after.ShouldBe(step);
            }
        }

        public class Close
        {
            [Fact]
            public void Closes_once_the_time_is_reached()
            {
                var step = SteamSendRules.Close(Armed);

                step.ShouldBe(CloseStep.Close);
            }

            [Fact]
            public void Waits_until_the_time()
            {
                var peer = Armed with { Now = new Seconds(9.99f) };

                var step = SteamSendRules.Close(peer);

                step.ShouldBe(CloseStep.Wait);
            }

            [Fact]
            public void Sets_the_time_once_the_queue_is_empty()
            {
                var peer = Armed with { Armed = false };

                var step = SteamSendRules.Close(peer);

                step.ShouldBe(CloseStep.Arm);
            }

            [Fact]
            public void Waits_while_messages_wait()
            {
                var peer = Armed with { Waiting = true };

                var step = SteamSendRules.Close(peer);

                step.ShouldBe(CloseStep.Wait);
            }
        }

        public class CloseAt
        {
            [Fact]
            public void Closes_a_second_after_the_queue_empties()
            {
                var at = SteamSendRules.CloseAt(new Seconds(4f));

                at.Value.ShouldBe(5f);
            }
        }
    }
}
