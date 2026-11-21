using DarkwoodMP.World.Talk;

namespace DarkwoodMP.Tests.World.Talk
{
    public class DialogueRulesTests
    {
        /// An open dialogue whose NPC is there and on, at 100 s.
        static readonly DialogueFacts Talking = new DialogueFacts
        {
            Open = true,
            Active = true,
            Together = true,
            Now = new Seconds(100f),
        };

        /// Its NPC has been gone since 96 s: exactly StuckAfter.
        static readonly DialogueWatch GoneAt96 = new DialogueWatch
        {
            Gone = true,
            GoneSince = new Seconds(96f),
        };

        /// Its NPC has been off since 99 s: exactly OffAfter.
        static readonly DialogueWatch OffAt99 = new DialogueWatch
        {
            Off = true,
            OffSince = new Seconds(99f),
        };

        /// A window that can be closed the game's way.
        static readonly DialogueCloseFacts Closable = new DialogueCloseFacts
        {
            HasWindow = true,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_dialogue_limits()
            {
                var limits = new[]
                {
                    DialogueRules.StuckAfter,
                    DialogueRules.OffAfter,
                };

                limits.ShouldBe(new[] { 4f, 1f });
            }
        }

        public class Watch
        {
            [Fact]
            public void Does_nothing_while_the_npc_is_there()
            {
                var tick = DialogueRules.Watch(Talking, default);

                tick.ShouldBe(new DialogueTick());
            }

            [Fact]
            public void Forgets_the_watch_with_no_dialogue_open()
            {
                var dialogue = Talking with { Open = false, Gone = true };

                var tick = DialogueRules.Watch(dialogue, GoneAt96);

                tick.ShouldBe(new DialogueTick());
            }

            [Fact]
            public void Starts_the_watch_when_the_npc_goes()
            {
                var dialogue = Talking with { Gone = true };

                var tick = DialogueRules.Watch(dialogue, default);

                tick.Watch.ShouldBe(new DialogueWatch
                {
                    Gone = true,
                    GoneSince = new Seconds(100f),
                });
            }

            [Fact]
            public void Forgets_the_gone_time_once_the_npc_is_back()
            {
                var tick = DialogueRules.Watch(Talking, GoneAt96);

                tick.Watch.ShouldBe(default(DialogueWatch));
            }

            [Fact]
            public void Waits_with_the_npc_gone_exactly_the_limit()
            {
                var dialogue = Talking with { Gone = true };

                var tick = DialogueRules.Watch(dialogue, GoneAt96);

                tick.Action.ShouldBe(DialogueAction.None);
            }

            [Fact]
            public void Forces_it_closed_just_past_the_limit()
            {
                var dialogue = Talking with
                {
                    Gone = true,
                    Now = new Seconds(100.01f),
                };

                var tick = DialogueRules.Watch(dialogue, GoneAt96);

                tick.Action.ShouldBe(DialogueAction.ForceGone);
            }

            [Fact]
            public void Forces_it_closed_while_tweening_once_stuck()
            {
                var dialogue = Talking with
                {
                    Gone = true,
                    Tweening = true,
                    Now = new Seconds(101f),
                };

                var tick = DialogueRules.Watch(dialogue, GoneAt96);

                tick.Action.ShouldBe(DialogueAction.ForceGone);
            }

            [Fact]
            public void Keeps_the_watch_it_forces_with()
            {
                var dialogue = Talking with
                {
                    Gone = true,
                    Now = new Seconds(101f),
                };

                var tick = DialogueRules.Watch(dialogue, GoneAt96);

                tick.Watch.ShouldBe(GoneAt96);
            }

            [Fact]
            public void Forces_it_closed_at_once_when_destroyed()
            {
                var dialogue = Talking with { Gone = true, Destroyed = true };

                var tick = DialogueRules.Watch(dialogue, default);

                tick.Action.ShouldBe(DialogueAction.ForceDestroyed);
            }

            [Fact]
            public void Waits_for_the_tween_when_destroyed()
            {
                var dialogue = Talking with
                {
                    Gone = true,
                    Destroyed = true,
                    Tweening = true,
                };

                var tick = DialogueRules.Watch(dialogue, default);

                tick.Action.ShouldBe(DialogueAction.None);
            }

            [Fact]
            public void Names_destroyed_before_stuck()
            {
                var dialogue = Talking with
                {
                    Gone = true,
                    Destroyed = true,
                    Now = new Seconds(101f),
                };

                var tick = DialogueRules.Watch(dialogue, GoneAt96);

                tick.Action.ShouldBe(DialogueAction.ForceDestroyed);
            }

            [Fact]
            public void Starts_the_watch_when_the_npc_is_switched_off()
            {
                var dialogue = Talking with { Active = false };

                var tick = DialogueRules.Watch(dialogue, default);

                tick.Watch.ShouldBe(new DialogueWatch
                {
                    Off = true,
                    OffSince = new Seconds(100f),
                });
            }

            [Fact]
            public void Waits_with_the_npc_off_exactly_the_limit()
            {
                var dialogue = Talking with { Active = false };

                var tick = DialogueRules.Watch(dialogue, OffAt99);

                tick.Action.ShouldBe(DialogueAction.None);
            }

            [Fact]
            public void Closes_it_with_the_npc_off_just_past_the_limit()
            {
                var dialogue = Talking with
                {
                    Active = false,
                    Now = new Seconds(100.01f),
                };

                var tick = DialogueRules.Watch(dialogue, OffAt99);

                tick.Action.ShouldBe(DialogueAction.Close);
            }

            [Fact]
            public void Tries_again_a_second_after_closing()
            {
                var dialogue = Talking with
                {
                    Active = false,
                    Now = new Seconds(101f),
                };

                var tick = DialogueRules.Watch(dialogue, OffAt99);

                tick.Watch.OffSince.Value.ShouldBe(101f);
            }

            [Fact]
            public void Waits_for_the_tween_with_the_npc_off()
            {
                var dialogue = Talking with
                {
                    Active = false,
                    Tweening = true,
                    Now = new Seconds(101f),
                };

                var tick = DialogueRules.Watch(dialogue, OffAt99);

                tick.Action.ShouldBe(DialogueAction.None);
            }

            [Fact]
            public void Leaves_an_npc_off_when_not_together()
            {
                var dialogue = Talking with
                {
                    Active = false,
                    Together = false,
                    Now = new Seconds(101f),
                };

                var tick = DialogueRules.Watch(dialogue, OffAt99);

                tick.ShouldBe(new DialogueTick());
            }

            [Fact]
            public void Counts_a_gone_npc_as_not_off()
            {
                var dialogue = Talking with { Gone = true, Active = false };

                var tick = DialogueRules.Watch(dialogue, OffAt99);

                tick.Watch.Off.ShouldBeFalse();
            }
        }

        public class CloseWay
        {
            [Fact]
            public void Closes_it_the_game_way()
            {
                var way = DialogueRules.CloseWay(Closable);

                way.ShouldBe(DialogueClose.Close);
            }

            [Fact]
            public void Closes_the_trade_first_in_a_shop()
            {
                var close = Closable with { InShop = true };

                var way = DialogueRules.CloseWay(close);

                way.ShouldBe(DialogueClose.CloseTrade);
            }

            [Fact]
            public void Does_nothing_with_no_window()
            {
                var close = Closable with
                {
                    HasWindow = false,
                    Destroyed = true,
                };

                var way = DialogueRules.CloseWay(close);

                way.ShouldBe(DialogueClose.Busy);
            }

            [Fact]
            public void Does_nothing_while_tweening()
            {
                var close = Closable with { Tweening = true, Destroyed = true };

                var way = DialogueRules.CloseWay(close);

                way.ShouldBe(DialogueClose.Busy);
            }

            [Fact]
            public void Forces_it_closed_when_destroyed()
            {
                var close = Closable with { Destroyed = true, Cleared = true };

                var way = DialogueRules.CloseWay(close);

                way.ShouldBe(DialogueClose.Force);
            }

            [Fact]
            public void Does_nothing_with_the_npc_cleared()
            {
                var close = Closable with { Cleared = true, InShop = true };

                var way = DialogueRules.CloseWay(close);

                way.ShouldBe(DialogueClose.Cleared);
            }
        }

        public class Closed
        {
            [Theory]
            [InlineData(DialogueClose.Busy, false)]
            [InlineData(DialogueClose.Force, true)]
            [InlineData(DialogueClose.Cleared, false)]
            [InlineData(DialogueClose.CloseTrade, true)]
            [InlineData(DialogueClose.Close, true)]
            public void Says_whether_it_did_something(
                DialogueClose way,
                bool closed)
            {
                var result = DialogueRules.Closed(way);

                result.ShouldBe(closed);
            }
        }

        public class RunsCloseTrade
        {
            [Theory]
            [InlineData(false, true)]
            [InlineData(true, false)]
            public void Skips_it_with_the_npc_destroyed(
                bool destroyed,
                bool runs)
            {
                var result = DialogueRules.RunsCloseTrade(destroyed);

                result.ShouldBe(runs);
            }
        }

        public class RunsClose
        {
            [Theory]
            [InlineData(false, false, true)]
            [InlineData(false, true, true)]
            [InlineData(true, false, false)]
            [InlineData(true, true, true)]
            public void Skips_it_with_the_npc_destroyed_unless_tweening(
                bool destroyed,
                bool tweening,
                bool runs)
            {
                var result = DialogueRules.RunsClose(destroyed, tweening);

                result.ShouldBe(runs);
            }
        }

        public class RunsFinishedLine
        {
            static readonly LineFacts Writing = new LineFacts
            {
                HasDialogue = true,
                HasNpc = true,
                Active = true,
            };

            [Fact]
            public void Runs_it_in_an_open_dialogue()
            {
                var runs = DialogueRules.RunsFinishedLine(Writing);

                runs.ShouldBeTrue();
            }

            [Fact]
            public void Runs_it_with_board_elements_left()
            {
                var line = new LineFacts { Elements = 1 };

                var runs = DialogueRules.RunsFinishedLine(line);

                runs.ShouldBeTrue();
            }

            [Fact]
            public void Skips_it_with_no_dialogue()
            {
                var line = Writing with { HasDialogue = false };

                var runs = DialogueRules.RunsFinishedLine(line);

                runs.ShouldBeFalse();
            }

            [Fact]
            public void Skips_it_with_no_npc()
            {
                var line = Writing with { HasNpc = false };

                var runs = DialogueRules.RunsFinishedLine(line);

                runs.ShouldBeFalse();
            }

            [Fact]
            public void Skips_it_on_a_closed_window()
            {
                var line = Writing with { Active = false };

                var runs = DialogueRules.RunsFinishedLine(line);

                runs.ShouldBeFalse();
            }
        }

        public class UndoesTrade
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(false, true, true)]
            [InlineData(false, false, false)]
            public void Undoes_a_trade_in_a_shop_or_its_window(
                bool shop,
                bool trading,
                bool undoes)
            {
                var result = DialogueRules.UndoesTrade(shop, trading);

                result.ShouldBe(undoes);
            }
        }
    }
}
