using System.Collections.Generic;
using DarkwoodMP.World.Inventories;

namespace DarkwoodMP.Tests.World.Inventories
{
    public partial class SlotRulesTests
    {
        const string Planks = "plank|3|1000|0|0";
        const string Meat = "meat|2|500|0|0";
        const string Rotten = "meat|2|400|0|0";

        static readonly SlotItem Rots = new SlotItem
        {
            Filled = true,
            Type = "meat",
            HasBase = true,
            Rots = true,
            MaxDurability = 1f,
        };

        /// One change the host can take: slot 1 still holds what the
        /// client saw, and the new item fits a stack.
        static readonly SlotChangeFacts Took = new SlotChangeFacts
        {
            Slot = 1,
            Old = Planks,
            HostKey = Planks,
            Fits = true,
        };

        static SlotBatchFacts Batch(params SlotChangeFacts[] changes)
        {
            return new SlotBatchFacts { SlotCount = 4, Changes = changes };
        }

        /// A slot we saw holding planks, still holding them, which the host
        /// says holds planks too.
        static readonly SlotFacts Same = new SlotFacts
        {
            Seen = true,
            SeenKey = Planks,
            NowKey = Planks,
            HostKey = Planks,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_wait_and_how_long_batches_are_kept()
            {
                var limits = new[] { SlotRules.AskTimeout, SlotRules.SentKept };

                limits.ShouldBe(new[] { 3f, 15f });
            }
        }

        public class IsFirstLook
        {
            [Fact]
            public void Says_a_container_never_looked_at_is_new()
            {
                var first = SlotRules.IsFirstLook(null, 4);

                first.ShouldBeTrue();
            }

            [Fact]
            public void Says_a_container_with_other_slots_now_is_new()
            {
                var first = SlotRules.IsFirstLook(new string[3], 4);

                first.ShouldBeTrue();
            }

            [Fact]
            public void Says_a_container_seen_with_these_slots_isnt_new()
            {
                var first = SlotRules.IsFirstLook(new string[4], 4);

                first.ShouldBeFalse();
            }
        }

        public class Asks
        {
            [Theory]
            [InlineData(true, true, true, true)]
            [InlineData(false, true, true, false)]
            [InlineData(true, false, true, false)]
            [InlineData(true, true, false, false)]
            public void Asks_on_a_client_for_a_named_container(
                bool askHost,
                bool client,
                bool named,
                bool expected)
            {
                var asks = SlotRules.Asks(askHost, client, named);

                asks.ShouldBe(expected);
            }
        }

        public class StillWaiting
        {
            [Theory]
            [InlineData(12.9f, true)]
            [InlineData(13f, false)]
            [InlineData(13.1f, false)]
            public void Waits_up_to_the_timeout(float now, bool expected)
            {
                var waits = SlotRules.StillWaiting(
                    new Seconds(10f),
                    new Seconds(now));

                waits.ShouldBe(expected);
            }
        }

        public class Changed
        {
            [Fact]
            public void Lists_the_slots_that_differ_in_order()
            {
                var before = new[] { "", Planks, Meat, "" };
                var now = new[] { Meat, Planks, "", "" };

                var slots = SlotRules.Changed(before, now);

                slots.ShouldBe(new[] { 0, 2 });
            }
        }

        public class Emptied
        {
            [Fact]
            public void Lists_the_slots_that_held_something()
            {
                var before = new[] { "", Planks, "", Meat };

                var slots = SlotRules.Emptied(before);

                slots.ShouldBe(new[] { 1, 3 });
            }
        }

        public class Drift
        {
            [Fact]
            public void Takes_the_worlds_change_to_a_slot_we_left()
            {
                var keys = new[] { Meat };

                var kept = SlotRules.Drift(
                    keys,
                    new[] { Meat },
                    new[] { Rotten });

                kept.ShouldBe(new[] { Rotten });
            }

            [Fact]
            public void Keeps_a_slot_our_player_changed()
            {
                var keys = new[] { "" };

                var kept = SlotRules.Drift(
                    keys,
                    new[] { Meat },
                    new[] { Rotten });

                kept.ShouldBe(new[] { "" });
            }

            [Fact]
            public void Stops_at_the_shortest_of_the_three()
            {
                var keys = new[] { Meat, Meat };

                var kept = SlotRules.Drift(
                    keys,
                    new[] { Meat, Meat },
                    new[] { Rotten });

                kept.ShouldBe(new[] { Rotten, Meat });
            }

            [Fact]
            public void Leaves_the_keys_given_as_they_were()
            {
                var keys = new[] { Meat };

                SlotRules.Drift(keys, new[] { Meat }, new[] { Rotten });

                keys.ShouldBe(new[] { Meat });
            }
        }

        public class Grown
        {
            [Fact]
            public void Adds_the_new_slots_as_they_are()
            {
                var keys = new[] { Meat };

                var grown = SlotRules.Grown(keys, new[] { Planks, "" });

                grown.ShouldBe(new[] { Meat, "" });
            }

            [Fact]
            public void Keeps_the_keys_of_a_container_that_didnt_grow()
            {
                var keys = new[] { Meat, "" };

                var grown = SlotRules.Grown(keys, new[] { Planks });

                grown.ShouldBeSameAs(keys);
            }
        }

        public class InRange
        {
            [Theory]
            [InlineData(3, true)]
            [InlineData(4, false)]
            public void Keeps_slots_under_the_count(int slot, bool expected)
            {
                var kept = SlotRules.InRange(slot, 4);

                kept.ShouldBe(expected);
            }
        }

        public class MayChange
        {
            [Fact]
            public void Allows_a_change_to_a_slot_as_the_client_saw_it()
            {
                var verdict = SlotRules.MayChange(Batch(Took));

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_what_the_container_refuses()
            {
                var batch = Batch(Took) with
                {
                    Container = Verdict.Refuse(Refusal.TooFar),
                };

                var verdict = SlotRules.MayChange(batch);

                verdict.Reason.ShouldBe(Refusal.TooFar);
            }

            [Fact]
            public void Refuses_a_container_another_player_has_open()
            {
                var batch = Batch(Took) with
                {
                    HeldByOther = true,
                    Holder = "Player 2",
                };

                var verdict = SlotRules.MayChange(batch);

                verdict.ShouldBe(Verdict.Refuse(Refusal.HasItOpen, "Player 2"));
            }

            [Fact]
            public void Allows_a_storage_another_player_has_open()
            {
                var batch = Batch(Took) with
                {
                    Storage = true,
                    HeldByOther = true,
                };

                var verdict = SlotRules.MayChange(batch);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Says_the_containers_reason_before_the_lock()
            {
                var batch = Batch(Took) with
                {
                    Container = Verdict.Refuse(Refusal.Locked),
                    HeldByOther = true,
                };

                var verdict = SlotRules.MayChange(batch);

                verdict.Reason.ShouldBe(Refusal.Locked);
            }

            [Fact]
            public void Refuses_a_lone_change_to_a_slot_that_changed()
            {
                var stale = Took with { HostKey = Meat };

                var verdict = SlotRules.MayChange(Batch(stale));

                verdict.ShouldBe(Verdict.Refuse(Refusal.JustChanged));
            }

            [Fact]
            public void Names_the_slot_and_the_count_of_a_batch_refused()
            {
                var stale = Took with { Slot = 2, HostKey = Meat };

                var verdict = SlotRules.MayChange(Batch(Took, stale));

                verdict.ShouldBe(Verdict.Refuse(
                    Refusal.SlotChanged,
                    Refusals.Detail("2", "2")));
            }

            [Fact]
            public void Names_the_first_slot_that_changed()
            {
                var first = Took with { Slot = 3, HostKey = Meat };
                var second = Took with { Slot = 0, HostKey = Meat };

                var verdict = SlotRules.MayChange(Batch(first, second));

                verdict.Detail.ShouldBe(Refusals.Detail("3", "2"));
            }

            [Fact]
            public void Allows_a_slot_that_only_rotted()
            {
                var rotted = Took with
                {
                    Old = Meat,
                    HostKey = Rotten,
                    Host = Rots,
                };

                var verdict = SlotRules.MayChange(Batch(rotted));

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Leaves_slots_past_the_count_out_of_the_checks()
            {
                var past = Took with { Slot = 4, HostKey = Meat, Fits = false };

                var verdict = SlotRules.MayChange(Batch(Took, past));

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Leaves_slots_past_the_count_out_of_the_count()
            {
                var stale = Took with { HostKey = Meat };
                var past = Took with { Slot = 9 };

                var verdict = SlotRules.MayChange(Batch(stale, past));

                verdict.Reason.ShouldBe(Refusal.JustChanged);
            }

            [Fact]
            public void Refuses_more_than_one_stack_holds()
            {
                var big = Took with { Slot = 2, Fits = false };

                var verdict = SlotRules.MayChange(Batch(Took, big));

                verdict.ShouldBe(Verdict.Refuse(Refusal.OverStack, "2"));
            }

            [Fact]
            public void Says_a_changed_slot_before_a_stack_too_big()
            {
                var big = Took with { Slot = 0, Fits = false };
                var stale = Took with { Slot = 2, HostKey = Meat };

                var verdict = SlotRules.MayChange(Batch(big, stale));

                verdict.Reason.ShouldBe(Refusal.SlotChanged);
            }

            [Fact]
            public void Says_the_lock_before_a_changed_slot()
            {
                var stale = Took with { HostKey = Meat };
                var batch = Batch(stale) with { HeldByOther = true };

                var verdict = SlotRules.MayChange(batch);

                verdict.Reason.ShouldBe(Refusal.HasItOpen);
            }
        }

        public class TakeHost
        {
            [Fact]
            public void Takes_the_host_slot_we_didnt_change()
            {
                var slot = Same with { HostKey = Meat };

                var step = SlotRules.TakeHost(slot);

                step.ShouldBe(SlotStep.Take);
            }

            [Fact]
            public void Takes_a_slot_we_havent_seen()
            {
                var slot = new SlotFacts { NowKey = Planks, HostKey = Meat };

                var step = SlotRules.TakeHost(slot);

                step.ShouldBe(SlotStep.Take);
            }

            [Fact]
            public void Keeps_our_change_when_the_host_has_what_we_saw()
            {
                var slot = Same with { NowKey = "" };

                var step = SlotRules.TakeHost(slot);

                step.ShouldBe(SlotStep.Keep);
            }

            [Fact]
            public void Keeps_our_change_when_the_host_differs_by_rot()
            {
                var slot = new SlotFacts
                {
                    Seen = true,
                    SeenKey = Meat,
                    NowKey = "",
                    HostKey = Rotten,
                    Here = Rots,
                };

                var step = SlotRules.TakeHost(slot);

                step.ShouldBe(SlotStep.Keep);
            }

            [Fact]
            public void Undoes_our_change_to_a_slot_another_player_changed()
            {
                var slot = Same with { NowKey = "", HostKey = Meat };

                var step = SlotRules.TakeHost(slot);

                step.ShouldBe(SlotStep.TakeAndUndo);
            }
        }

        public class Merge
        {
            [Fact]
            public void Leaves_a_slot_that_holds_the_hosts()
            {
                var step = SlotRules.Merge(Same);

                step.ShouldBe(SlotStep.None);
            }

            [Fact]
            public void Takes_the_host_slot_we_didnt_change()
            {
                var slot = Same with { HostKey = Meat };

                var step = SlotRules.Merge(slot);

                step.ShouldBe(SlotStep.Take);
            }

            [Fact]
            public void Takes_a_slot_we_havent_seen_that_differs()
            {
                var slot = new SlotFacts { NowKey = Planks, HostKey = Meat };

                var step = SlotRules.Merge(slot);

                step.ShouldBe(SlotStep.Take);
            }

            [Fact]
            public void Keeps_our_change_when_the_host_has_what_we_saw()
            {
                var slot = Same with { NowKey = "" };

                var step = SlotRules.Merge(slot);

                step.ShouldBe(SlotStep.Keep);
            }

            [Fact]
            public void Undoes_our_change_to_a_slot_that_changed_on_the_host()
            {
                var slot = Same with { NowKey = "", HostKey = Meat };

                var step = SlotRules.Merge(slot);

                step.ShouldBe(SlotStep.TakeAndUndo);
            }
        }

        public class HasUnsent
        {
            [Theory]
            [InlineData(true, "", true)]
            [InlineData(true, Planks, false)]
            [InlineData(false, "", false)]
            public void Finds_a_change_since_the_last_look(
                bool seen,
                string now,
                bool expected)
            {
                var slot = Same with { Seen = seen, NowKey = now };

                var unsent = SlotRules.HasUnsent(slot);

                unsent.ShouldBe(expected);
            }
        }

        public class UndoOnRefusal
        {
            [Theory]
            [InlineData(false, false, RefusalUndo.Alone)]
            [InlineData(false, true, RefusalUndo.Alone)]
            [InlineData(true, false, RefusalUndo.Batch)]
            [InlineData(true, true, RefusalUndo.None)]
            public void Undoes_a_batch_once(
                bool inBatch,
                bool undone,
                RefusalUndo expected)
            {
                var undo = SlotRules.UndoOnRefusal(inBatch, undone);

                undo.ShouldBe(expected);
            }
        }
    }
}
