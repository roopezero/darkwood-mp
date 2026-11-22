using DarkwoodMP.World;
using DarkwoodMP.World.Locks;

namespace DarkwoodMP.Tests.World.Locks
{
    public partial class LockRulesTests
    {
        static readonly ReachFacts AtReach = Requesters.AtReach;

        static readonly ReachFacts Far = Requesters.PastReach;

        public class Limits
        {
            [Fact]
            public void Pins_how_a_lock_is_opened()
            {
                var how = new[]
                {
                    LockRules.ByStory,
                    LockRules.ByKey,
                    LockRules.ByLockpick,
                };

                how.ShouldBe(new[] { 0f, 1f, 2f });
            }
        }

        public class MayUnlock
        {
            /// A locked chest opened with a key the host's journal holds.
            static readonly LockFacts Chest = new LockFacts
            {
                Exists = true,
                Locked = true,
                How = LockRules.ByKey,
                KeyKnown = true,
                KeyType = "key_shed",
                Requester = AtReach,
            };

            [Fact]
            public void Allows_a_key_somebody_has()
            {
                var verdict = LockRules.MayUnlock(Chest);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_lock_that_isnt_here()
            {
                var lck = Chest with { Exists = false };

                var verdict = LockRules.MayUnlock(lck);

                verdict.Reason.ShouldBe(Refusal.NoSuchLock);
            }

            [Fact]
            public void Refuses_a_lock_just_past_the_reach()
            {
                var lck = Chest with { Requester = Far };

                var verdict = LockRules.MayUnlock(lck);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Refuses_a_lock_that_isnt_locked()
            {
                var lck = Chest with { Locked = false };

                var verdict = LockRules.MayUnlock(lck);

                verdict.Reason.ShouldBe(Refusal.NotLocked);
            }

            [Fact]
            public void Refuses_what_only_the_story_does()
            {
                var lck = Chest with { How = LockRules.ByStory };

                var verdict = LockRules.MayUnlock(lck);

                verdict.Reason.ShouldBe(Refusal.StoryOnly);
            }

            [Fact]
            public void Refuses_a_key_nobody_has()
            {
                var lck = Chest with { KeyKnown = false };

                var verdict = LockRules.MayUnlock(lck);

                verdict.ShouldBe(Verdict.Refuse(Refusal.NoKey, "key_shed"));
            }

            [Theory]
            [InlineData(2f)]
            [InlineData(3f)]
            [InlineData(0.5f)]
            [InlineData(-1f)]
            public void Allows_any_other_way_unchecked(float how)
            {
                var lck = Chest with { How = how, KeyKnown = false };

                var verdict = LockRules.MayUnlock(lck);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Puts_the_reach_before_the_state()
            {
                var lck = Chest with { Requester = Far, Locked = false };

                var verdict = LockRules.MayUnlock(lck);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Puts_an_open_lock_before_the_story()
            {
                var lck = Chest with
                {
                    Locked = false,
                    How = LockRules.ByStory,
                };

                var verdict = LockRules.MayUnlock(lck);

                verdict.Reason.ShouldBe(Refusal.NotLocked);
            }
        }

        public class MaySearch
        {
            /// A cupboard with an inventory, exactly at the reach.
            static readonly SearchFacts Cupboard = new SearchFacts
            {
                Exists = true,
                HasInventory = true,
                Requester = AtReach,
            };

            [Fact]
            public void Allows_a_container_in_reach()
            {
                var verdict = LockRules.MaySearch(Cupboard);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Puts_an_unpicked_crate_before_a_missing_inventory()
            {
                var box = Cupboard with
                {
                    Crate = true,
                    Searched = false,
                    HasInventory = false,
                };

                var verdict = LockRules.MaySearch(box);

                verdict.Reason.ShouldBe(Refusal.HostRollsCrates);
            }

            [Fact]
            public void Refuses_a_container_that_isnt_here()
            {
                var box = Cupboard with { Exists = false };

                var verdict = LockRules.MaySearch(box);

                verdict.Reason.ShouldBe(Refusal.NoSuchThing);
            }

            [Fact]
            public void Refuses_a_crate_not_picked_open()
            {
                var box = Cupboard with { Crate = true };

                var verdict = LockRules.MaySearch(box);

                verdict.Reason.ShouldBe(Refusal.HostRollsCrates);
            }

            [Fact]
            public void Allows_a_crate_picked_open()
            {
                var box = Cupboard with { Crate = true, Searched = true };

                var verdict = LockRules.MaySearch(box);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_thing_with_nothing_to_search()
            {
                var box = Cupboard with { HasInventory = false };

                var verdict = LockRules.MaySearch(box);

                verdict.Reason.ShouldBe(Refusal.NothingToSearch);
            }

            [Fact]
            public void Refuses_a_container_just_past_the_reach()
            {
                var box = Cupboard with { Requester = Far };

                var verdict = LockRules.MaySearch(box);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Puts_the_reach_last()
            {
                var box = Cupboard with { Requester = Far, Crate = true };

                var verdict = LockRules.MaySearch(box);

                verdict.Reason.ShouldBe(Refusal.HostRollsCrates);
            }
        }

        public class MayUnlockPadlock
        {
            /// A locked padlock opened with its own code.
            static readonly PadlockFacts Pad = new PadlockFacts
            {
                Exists = true,
                Locked = true,
                Manual = true,
                Code = "1234",
                Combination = "1234",
                Requester = AtReach,
            };

            [Fact]
            public void Allows_the_padlock_code()
            {
                var verdict = LockRules.MayUnlockPadlock(Pad);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Puts_the_reach_before_the_story()
            {
                var pad = Pad with { Manual = false, Requester = Far };

                var verdict = LockRules.MayUnlockPadlock(pad);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Refuses_a_padlock_that_isnt_here()
            {
                var pad = Pad with { Exists = false };

                var verdict = LockRules.MayUnlockPadlock(pad);

                verdict.Reason.ShouldBe(Refusal.NoSuchPadlock);
            }

            [Fact]
            public void Refuses_a_padlock_just_past_the_reach()
            {
                var pad = Pad with { Requester = Far };

                var verdict = LockRules.MayUnlockPadlock(pad);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Refuses_an_opening_without_a_code()
            {
                var pad = Pad with { Manual = false };

                var verdict = LockRules.MayUnlockPadlock(pad);

                verdict.Reason.ShouldBe(Refusal.StoryOnly);
            }

            [Fact]
            public void Refuses_the_wrong_code()
            {
                var pad = Pad with { Code = "0000" };

                var verdict = LockRules.MayUnlockPadlock(pad);

                verdict.Reason.ShouldBe(Refusal.WrongCode);
            }

            [Fact]
            public void Allows_any_code_on_an_open_padlock()
            {
                var pad = Pad with { Locked = false, Code = "0000" };

                var verdict = LockRules.MayUnlockPadlock(pad);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Puts_the_story_before_the_code()
            {
                var pad = Pad with { Manual = false, Code = "0000" };

                var verdict = LockRules.MayUnlockPadlock(pad);

                verdict.Reason.ShouldBe(Refusal.StoryOnly);
            }
        }

        public class MayPick
        {
            static readonly CrateFacts Crate = new CrateFacts
            {
                Crate = true,
                Requester = AtReach,
            };

            [Fact]
            public void Allows_a_crate_in_reach()
            {
                var verdict = LockRules.MayPick(Crate);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_what_isnt_a_crate()
            {
                var item = Crate with { Crate = false, Requester = Far };

                var verdict = LockRules.MayPick(item);

                verdict.Reason.ShouldBe(Refusal.NoLockToPick);
            }

            [Fact]
            public void Refuses_a_crate_just_past_the_reach()
            {
                var item = Crate with { Requester = Far };

                var verdict = LockRules.MayPick(item);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }
        }

        public class IsCrate
        {
            [Theory]
            [InlineData(5f, true, false, true)]
            [InlineData(0f, true, false, false)]
            [InlineData(5f, false, false, false)]
            [InlineData(5f, true, true, false)]
            public void Counts_a_picked_lock_on_an_inventory_without_a_trap(
                float time,
                bool inventory,
                bool trigger,
                bool crate)
            {
                var answer = LockRules.IsCrate(time, inventory, trigger);

                answer.ShouldBe(crate);
            }
        }

        public class Pick
        {
            [Fact]
            public void Opens_a_crate_for_a_roll_under_the_chance()
            {
                var outcome = LockRules.Pick(false, 0.5f, 0.49f);

                outcome.ShouldBe(PickOutcome.Opened);
            }

            [Fact]
            public void Fails_a_roll_exactly_at_the_chance()
            {
                var outcome = LockRules.Pick(false, 0.5f, 0.5f);

                outcome.ShouldBe(PickOutcome.Failed);
            }

            [Fact]
            public void Counts_a_searched_crate_as_open_whatever_the_roll()
            {
                var outcome = LockRules.Pick(true, 0f, 1f);

                outcome.ShouldBe(PickOutcome.AlreadyOpen);
            }
        }
    }
}
