using DarkwoodMP.World.Talk;

namespace DarkwoodMP.Tests.World.Talk
{
    public partial class TalkRulesTests
    {
        /// The host's own player about to talk to an NPC nobody holds.
        static readonly HostTalkFacts HostTalk = new HostTalkFacts
        {
            HasDialogue = true,
            WantsToTalk = true,
            Sharing = true,
            IsHost = true,
            RefValid = true,
            Holder = 5,
            Local = 1,
            HolderName = "Ann",
        };

        /// A client's dialogue that closed while it was a client's copy.
        static readonly TalkEndFacts ClientEnd = new TalkEndFacts
        {
            Ours = true,
            OursValid = true,
            HasSession = true,
            ClientMode = true,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_talk_limits()
            {
                var limits = new[]
                {
                    TalkRules.Grace,
                    TalkRules.Reach,
                    TalkRules.RetryEvery,
                };

                limits.ShouldBe(new[] { 1.5f, 1500f, 0.25f });
            }

            [Fact]
            public void Pins_the_tries()
            {
                TalkRules.Tries.ShouldBe(40);
            }
        }

        public class LockName
        {
            [Fact]
            public void Locks_a_trader_by_name()
            {
                var npc = new TalkNameFacts { Name = "Piotrek", Trader = true };

                var name = TalkRules.LockName(npc);

                name.ShouldBe("Piotrek");
            }

            [Theory]
            [InlineData("Wolfman")]
            [InlineData("the_WOLF")]
            [InlineData("wolf")]
            public void Locks_the_wolf_by_name_in_any_case(string wolf)
            {
                var npc = new TalkNameFacts { Name = wolf };

                var name = TalkRules.LockName(npc);

                name.ShouldBe(wolf);
            }

            [Fact]
            public void Locks_a_trader_from_the_shared_save_by_ref()
            {
                var npc = new TalkNameFacts
                {
                    Name = "wolfman",
                    Trader = true,
                    Saved = true,
                };

                var name = TalkRules.LockName(npc);

                name.ShouldBe("");
            }

            [Fact]
            public void Locks_any_other_npc_by_ref()
            {
                var npc = new TalkNameFacts { Name = "Doctor" };

                var name = TalkRules.LockName(npc);

                name.ShouldBe("");
            }

            [Fact]
            public void Locks_a_trader_with_no_name_by_ref()
            {
                var npc = new TalkNameFacts { Trader = true };

                var name = TalkRules.LockName(npc);

                name.ShouldBe("");
            }
        }

        public class Key
        {
            [Fact]
            public void Keys_a_named_npc_by_name()
            {
                var key = TalkRules.Key("Piotrek", "Saved:12");

                key.ShouldBe("npc:Piotrek");
            }

            [Fact]
            public void Keys_any_other_by_its_ref()
            {
                var key = TalkRules.Key("", "Saved:12");

                key.ShouldBe("Saved:12");
            }
        }

        public class HeldByOther
        {
            [Theory]
            [InlineData(true, 5UL, 6UL, true)]
            [InlineData(true, 5UL, 5UL, false)]
            [InlineData(false, 5UL, 6UL, false)]
            public void Finds_a_holder_other_than_who(
                bool held,
                ulong holder,
                ulong who,
                bool other)
            {
                var result = TalkRules.HeldByOther(held, holder, who);

                result.ShouldBe(other);
            }
        }

        public class Releases
        {
            [Theory]
            [InlineData(true, 5UL, 5UL, true)]
            [InlineData(true, 5UL, 6UL, false)]
            [InlineData(false, 0UL, 0UL, false)]
            public void Counts_only_the_holder_release(
                bool held,
                ulong holder,
                ulong who,
                bool releases)
            {
                var result = TalkRules.Releases(held, holder, who);

                result.ShouldBe(releases);
            }
        }

        public class HostMayTalk
        {
            [Fact]
            public void Lets_the_host_talk_to_a_free_npc()
            {
                var verdict = TalkRules.HostMayTalk(HostTalk);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_while_a_joiner_talks_to_them()
            {
                var talk = HostTalk with { Held = true };

                var verdict = TalkRules.HostMayTalk(talk);

                verdict.ShouldBe(Verdict.Refuse(Refusal.OtherTalking, "Ann"));
            }

            [Fact]
            public void Words_it_as_the_host_shows_it()
            {
                var talk = HostTalk with { Held = true };

                var text = Refusals.Text(TalkRules.HostMayTalk(talk));

                text.ShouldBe("Ann is talking to them");
            }

            [Fact]
            public void Lets_the_host_talk_to_one_it_holds()
            {
                var talk = HostTalk with { Held = true, Holder = 1 };

                var verdict = TalkRules.HostMayTalk(talk);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Lets_an_npc_with_no_dialogue_run()
            {
                var talk = HostTalk with { Held = true, HasDialogue = false };

                var verdict = TalkRules.HostMayTalk(talk);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Lets_an_npc_that_does_not_want_to_talk_run()
            {
                var talk = HostTalk with { Held = true, WantsToTalk = false };

                var verdict = TalkRules.HostMayTalk(talk);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Lets_it_run_when_nothing_is_shared()
            {
                var talk = HostTalk with { Held = true, Sharing = false };

                var verdict = TalkRules.HostMayTalk(talk);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Lets_a_client_talk_unchecked()
            {
                var talk = HostTalk with { Held = true, IsHost = false };

                var verdict = TalkRules.HostMayTalk(talk);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Lets_an_npc_with_no_ref_run()
            {
                var talk = HostTalk with { Held = true, RefValid = false };

                var verdict = TalkRules.HostMayTalk(talk);

                verdict.ShouldBe(Verdict.Allow);
            }
        }

        public class Opens
        {
            static readonly TalkOpenFacts Shared = new TalkOpenFacts
            {
                Sharing = true,
                RefValid = true,
            };

            [Fact]
            public void Asks_the_host_as_a_client()
            {
                var open = TalkRules.Opens(Shared);

                open.ShouldBe(TalkOpen.Ask);
            }

            [Fact]
            public void Takes_the_lock_as_the_host()
            {
                var talk = Shared with { IsHost = true };

                var open = TalkRules.Opens(talk);

                open.ShouldBe(TalkOpen.Take);
            }

            [Fact]
            public void Shares_nothing_when_not_sharing()
            {
                var talk = Shared with { Sharing = false };

                var open = TalkRules.Opens(talk);

                open.ShouldBe(TalkOpen.NotShared);
            }

            [Fact]
            public void Shares_nothing_of_a_dream_npc_of_our_own()
            {
                var talk = Shared with { OursAlone = true, IsHost = true };

                var open = TalkRules.Opens(talk);

                open.ShouldBe(TalkOpen.NotShared);
            }

            [Fact]
            public void Shares_nothing_of_an_npc_with_no_ref()
            {
                var talk = Shared with { RefValid = false };

                var open = TalkRules.Opens(talk);

                open.ShouldBe(TalkOpen.NotShared);
            }
        }

        public class Ends
        {
            [Fact]
            public void Sends_the_release_as_a_client_copy()
            {
                var end = TalkRules.Ends(ClientEnd);

                end.ShouldBe(TalkEnd.Send);
            }

            [Fact]
            public void Releases_it_here_as_the_host()
            {
                var talk = ClientEnd with { IsHost = true };

                var end = TalkRules.Ends(talk);

                end.ShouldBe(TalkEnd.Release);
            }

            [Fact]
            public void Only_flushes_as_a_client_no_longer_a_copy()
            {
                var talk = ClientEnd with { ClientMode = false };

                var end = TalkRules.Ends(talk);

                end.ShouldBe(TalkEnd.Flush);
            }

            [Fact]
            public void Does_nothing_with_nothing_kept()
            {
                var talk = ClientEnd with { Ours = false, OursValid = false };

                var end = TalkRules.Ends(talk);

                end.ShouldBe(TalkEnd.None);
            }

            [Fact]
            public void Releases_a_ref_kept_after_its_npc_went()
            {
                var talk = ClientEnd with { Ours = false };

                var end = TalkRules.Ends(talk);

                end.ShouldBe(TalkEnd.Send);
            }

            [Fact]
            public void Only_forgets_with_no_session()
            {
                var talk = ClientEnd with { HasSession = false };

                var end = TalkRules.Ends(talk);

                end.ShouldBe(TalkEnd.Forget);
            }

            [Fact]
            public void Only_forgets_an_npc_with_no_valid_ref()
            {
                var talk = ClientEnd with { OursValid = false, IsHost = true };

                var end = TalkRules.Ends(talk);

                end.ShouldBe(TalkEnd.Forget);
            }
        }

        public class UnloadEnds
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(true, true, false)]
            [InlineData(false, false, false)]
            public void Ends_a_dialogue_whose_npc_unloaded(
                bool valid,
                bool ours,
                bool ends)
            {
                var result = TalkRules.UnloadEnds(valid, ours);

                result.ShouldBe(ends);
            }
        }

        public class CloseWhenReady
        {
            static readonly TalkCloseFacts Opening = new TalkCloseFacts
            {
                SameNpc = true,
                Opening = true,
                Tries = 40,
            };

            [Fact]
            public void Waits_while_the_window_opens()
            {
                var next = TalkRules.CloseWhenReady(Opening);

                next.ShouldBe(CloseTry.Wait);
            }

            [Fact]
            public void Waits_with_one_try_left()
            {
                var close = Opening with { Tries = 1 };

                var next = TalkRules.CloseWhenReady(close);

                next.ShouldBe(CloseTry.Wait);
            }

            [Fact]
            public void Stops_waiting_with_no_tries_left()
            {
                var close = Opening with { Tries = 0 };

                var next = TalkRules.CloseWhenReady(close);

                next.ShouldBe(CloseTry.Stop);
            }

            [Fact]
            public void Closes_it_once_open()
            {
                var close = Opening with { Opening = false };

                var next = TalkRules.CloseWhenReady(close);

                next.ShouldBe(CloseTry.Close);
            }

            [Fact]
            public void Closes_it_whatever_the_tries_left()
            {
                var close = Opening with { Opening = false, Tries = -3 };

                var next = TalkRules.CloseWhenReady(close);

                next.ShouldBe(CloseTry.Close);
            }

            [Fact]
            public void Stops_once_the_window_has_another_npc()
            {
                var close = Opening with { SameNpc = false };

                var next = TalkRules.CloseWhenReady(close);

                next.ShouldBe(CloseTry.Stop);
            }
        }
    }
}
