using DarkwoodMP.World.Locks;

namespace DarkwoodMP.Tests.World.Locks
{
    /// What a game sends when its locks and containers open.
    public partial class LockRulesTests
    {
        public class SendsUnlock
        {
            /// A chest's lock just opened with a key, at the outermost call.
            static readonly UnlockSentFacts Chest = new UnlockSentFacts
            {
                Outermost = true,
                Sharing = true,
                WasLocked = true,
                Locked = false,
                How = LockRules.ByKey,
            };

            [Fact]
            public void Sends_a_chest_lock_as_an_item_unlock()
            {
                var sent = LockRules.SendsUnlock(Chest);

                sent.ShouldBe(UnlockMessage.ItemUnlock);
            }

            [Fact]
            public void Sends_a_door_lock_as_a_door_unlock()
            {
                var lck = Chest with { HasDoor = true };

                var sent = LockRules.SendsUnlock(lck);

                sent.ShouldBe(UnlockMessage.DoorUnlock);
            }

            [Fact]
            public void Sends_nothing_from_a_nested_call()
            {
                var lck = Chest with { Outermost = false };

                var sent = LockRules.SendsUnlock(lck);

                sent.ShouldBe(UnlockMessage.Nothing);
            }

            [Fact]
            public void Sends_nothing_while_not_sharing()
            {
                var lck = Chest with { Sharing = false };

                var sent = LockRules.SendsUnlock(lck);

                sent.ShouldBe(UnlockMessage.Nothing);
            }

            [Fact]
            public void Sends_nothing_for_a_lock_that_was_open()
            {
                var lck = Chest with { WasLocked = false, HasDoor = true };

                var sent = LockRules.SendsUnlock(lck);

                sent.ShouldBe(UnlockMessage.Nothing);
            }

            [Fact]
            public void Sends_nothing_for_a_lock_still_locked()
            {
                var lck = Chest with { Locked = true, HasDoor = true };

                var sent = LockRules.SendsUnlock(lck);

                sent.ShouldBe(UnlockMessage.Nothing);
            }
        }

        public class SendsKeyFirst
        {
            [Fact]
            public void Sends_the_key_first_from_a_client()
            {
                var lck = new UnlockSentFacts
                {
                    How = LockRules.ByKey,
                    Client = true,
                };

                LockRules.SendsKeyFirst(lck).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_the_key_to_the_host()
            {
                var lck = new UnlockSentFacts { How = LockRules.ByKey };

                LockRules.SendsKeyFirst(lck).ShouldBeFalse();
            }

            [Theory]
            [InlineData(LockRules.ByStory)]
            [InlineData(LockRules.ByLockpick)]
            public void Leaves_out_the_key_for_another_way(float how)
            {
                var lck = new UnlockSentFacts { How = how, Client = true };

                LockRules.SendsKeyFirst(lck).ShouldBeFalse();
            }
        }

        public class SendsPadlock
        {
            /// A padlock just opened with a code, at the outermost call.
            static readonly PadlockSentFacts Pad = new PadlockSentFacts
            {
                Outermost = true,
                Sharing = true,
                WasLocked = true,
                Locked = false,
                Manually = true,
                Code = "1234",
            };

            [Fact]
            public void Sends_a_padlock_that_opened()
            {
                LockRules.SendsPadlock(Pad).ShouldBeTrue();
            }

            [Fact]
            public void Sends_nothing_from_a_nested_call()
            {
                var pad = Pad with { Outermost = false };

                LockRules.SendsPadlock(pad).ShouldBeFalse();
            }

            [Fact]
            public void Sends_nothing_while_not_sharing()
            {
                var pad = Pad with { Sharing = false };

                LockRules.SendsPadlock(pad).ShouldBeFalse();
            }

            [Fact]
            public void Sends_nothing_for_a_padlock_that_was_open()
            {
                var pad = Pad with { WasLocked = false };

                LockRules.SendsPadlock(pad).ShouldBeFalse();
            }

            [Fact]
            public void Sends_nothing_for_a_padlock_still_locked()
            {
                var pad = Pad with { Locked = true };

                LockRules.SendsPadlock(pad).ShouldBeFalse();
            }
        }

        public class PadlockCode
        {
            [Fact]
            public void Sends_the_code_a_player_entered()
            {
                var pad = new PadlockSentFacts
                {
                    Manually = true,
                    Code = "1234",
                };

                LockRules.PadlockCode(pad).ShouldBe("1234");
            }

            [Fact]
            public void Sends_no_code_when_the_story_opened_it()
            {
                var pad = new PadlockSentFacts { Code = "1234" };

                LockRules.PadlockCode(pad).ShouldBe("");
            }
        }

        public class SendsCrateSearched
        {
            /// A crate the host just picked open, with no world call
            /// running.
            static readonly SearchSentFacts Crate = new SearchSentFacts
            {
                Searched = true,
                Crate = true,
                Depth = 0,
                Sharing = true,
                Host = true,
            };

            [Fact]
            public void Sends_a_crate_the_host_picked()
            {
                LockRules.SendsCrateSearched(Crate).ShouldBeTrue();
            }

            [Fact]
            public void Sends_nothing_for_a_crate_already_searched()
            {
                var item = Crate with { WasSearched = true };

                LockRules.SendsCrateSearched(item).ShouldBeFalse();
            }

            [Fact]
            public void Sends_nothing_when_the_pick_failed()
            {
                var item = Crate with { Searched = false };

                LockRules.SendsCrateSearched(item).ShouldBeFalse();
            }

            [Fact]
            public void Sends_nothing_for_what_isnt_a_crate()
            {
                var item = Crate with { Crate = false };

                LockRules.SendsCrateSearched(item).ShouldBeFalse();
            }

            [Fact]
            public void Sends_nothing_inside_a_world_call()
            {
                var item = Crate with { Depth = 1 };

                LockRules.SendsCrateSearched(item).ShouldBeFalse();
            }

            [Fact]
            public void Sends_nothing_while_not_sharing()
            {
                var item = Crate with { Sharing = false };

                LockRules.SendsCrateSearched(item).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_client_pick_to_the_host()
            {
                var item = Crate with { Host = false };

                LockRules.SendsCrateSearched(item).ShouldBeFalse();
            }
        }

        public class SendsSearched
        {
            /// A cupboard just opened, by either side.
            static readonly SearchSentFacts Cupboard = new SearchSentFacts
            {
                Searched = true,
                Sharing = true,
            };

            [Fact]
            public void Sends_a_container_that_became_searched()
            {
                LockRules.SendsSearched(Cupboard).ShouldBeTrue();
            }

            [Fact]
            public void Sends_nothing_for_one_already_searched()
            {
                var item = Cupboard with { WasSearched = true };

                LockRules.SendsSearched(item).ShouldBeFalse();
            }

            [Fact]
            public void Sends_nothing_for_one_still_unsearched()
            {
                var item = Cupboard with { Searched = false };

                LockRules.SendsSearched(item).ShouldBeFalse();
            }

            [Fact]
            public void Sends_nothing_while_not_sharing()
            {
                var item = Cupboard with { Sharing = false };

                LockRules.SendsSearched(item).ShouldBeFalse();
            }
        }
    }
}
