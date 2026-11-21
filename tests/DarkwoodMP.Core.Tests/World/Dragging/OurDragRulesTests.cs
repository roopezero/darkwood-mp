using DarkwoodMP.World.Dragging;

namespace DarkwoodMP.Tests.World.Dragging
{
    public class OurDragRulesTests
    {
        /// A client takes hold of a named thing nobody drags.
        static readonly StartFacts Taking = new StartFacts { Named = true };

        /// The host refused our claim on a thing we still drag.
        static readonly ReplyFacts Refused = new ReplyFacts
        {
            Same = true,
            Reason = "it isn't here",
        };

        /// The host, to us (player 3), says player 2 drags the thing we
        /// drag, and that we let go.
        static readonly NewsFacts Told = new NewsFacts
        {
            Client = true,
            FromHost = true,
            InWorld = true,
            Owner = 2,
            LetGo = true,
            Ours = true,
            Local = 3,
        };

        /// Our own drag, on a client (we are player 1).
        static readonly OwnerFacts Ours = new OwnerFacts
        {
            Named = true,
            Owned = true,
            Owner = 1,
            Local = 1,
        };

        /// A client's drag the host agreed to, its next move due now.
        static readonly SendFacts Send = new SendFacts
        {
            Dragging = true,
            Named = true,
            Claimed = true,
            Now = new Seconds(10f),
            Next = new Seconds(10f),
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_send_times()
            {
                var limits = new[]
                {
                    OurDragRules.SendInterval,
                    OurDragRules.SettleAfter,
                };

                limits.ShouldBe(new[] { 0.1f, 1.5f });
            }

            [Fact]
            public void Pins_the_end_of_the_owners_refusal()
            {
                OurDragRules.MovingSuffix.ShouldBe("is moving it");
            }
        }

        public class Start
        {
            [Fact]
            public void Claims_it_on_a_client()
            {
                OurDragRules.Start(Taking).ShouldBe(StartAction.Claim);
            }

            [Fact]
            public void Owns_it_on_the_host()
            {
                var start = Taking with { Host = true };

                OurDragRules.Start(start).ShouldBe(StartAction.Own);
            }

            [Fact]
            public void Sends_nothing_for_a_name_that_isnt_valid()
            {
                var start = Taking with { Named = false, Host = true };

                OurDragRules.Start(start).ShouldBe(StartAction.Unnamed);
            }

            [Fact]
            public void Lets_go_of_someone_elses_drag_first()
            {
                var start = Taking with { Other = 2, Named = false };

                OurDragRules.Start(start).ShouldBe(StartAction.Refused);
            }
        }

        public class Reply
        {
            [Fact]
            public void Claims_it_when_the_host_agrees()
            {
                var reply = new ReplyFacts { Same = true, Ok = true };

                OurDragRules.Reply(reply).ShouldBe(ClaimReply.Claimed);
            }

            [Fact]
            public void Ignores_an_answer_after_letting_go()
            {
                var reply = new ReplyFacts { Ok = true };

                OurDragRules.Reply(reply).ShouldBe(ClaimReply.Ignore);
            }

            [Fact]
            public void Shows_a_refusal_that_names_the_owner()
            {
                var reply = Refused with { Reason = "Anna is moving it" };

                OurDragRules.Reply(reply).ShouldBe(ClaimReply.ShowReason);
            }

            [Fact]
            public void Shows_the_hosts_refusal_of_someone_elses_drag()
            {
                var moving = Verdict.Refuse(Refusal.OtherMoving, "Anna");
                var reply = Refused with { Reason = Refusals.Text(moving) };

                OurDragRules.Reply(reply).ShouldBe(ClaimReply.ShowReason);
            }

            [Theory]
            [InlineData("it isn't here")]
            [InlineData("Box is 700 away from them (reach 600)")]
            [InlineData("the session ended")]
            [InlineData("Anna is moving it.")]
            public void Shows_too_far_for_any_other_refusal(string reason)
            {
                var reply = Refused with { Reason = reason };

                OurDragRules.Reply(reply).ShouldBe(ClaimReply.TooFar);
            }
        }

        public class News
        {
            [Fact]
            public void Lets_go_naming_the_new_owner()
            {
                var news = OurDragRules.News(Told);

                news.Drag.ShouldBe(ClaimReply.Moving);
            }

            [Fact]
            public void Remembers_the_new_owner()
            {
                var news = OurDragRules.News(Told);

                news.Entry.ShouldBe(OwnerEntry.Remember);
            }

            [Fact]
            public void Forgets_the_owner_when_nobody_drags_it()
            {
                var facts = Told with { Owner = 0, LetGo = false };

                var news = OurDragRules.News(facts);

                news.Entry.ShouldBe(OwnerEntry.Forget);
            }

            [Fact]
            public void Lets_go_as_too_far_when_told_to_with_no_owner()
            {
                var facts = Told with { Owner = 0 };

                var news = OurDragRules.News(facts);

                news.Drag.ShouldBe(ClaimReply.TooFar);
            }

            [Fact]
            public void Keeps_our_drag_when_not_told_to_let_go()
            {
                var facts = Told with { LetGo = false };

                var news = OurDragRules.News(facts);

                news.Drag.ShouldBe(ClaimReply.Ignore);
            }

            [Fact]
            public void Claims_our_drag_when_we_are_the_owner()
            {
                var facts = Told with { Owner = 3 };

                var news = OurDragRules.News(facts);

                news.Drag.ShouldBe(ClaimReply.Claimed);
            }

            [Fact]
            public void Leaves_our_drag_alone_for_news_of_another_thing()
            {
                var facts = Told with { Ours = false };

                var news = OurDragRules.News(facts);

                news.ShouldBe(new OwnerNews { Entry = OwnerEntry.Remember });
            }

            [Theory]
            [InlineData(false, true, true)]
            [InlineData(true, false, true)]
            [InlineData(true, true, false)]
            public void Ignores_news_a_client_doesnt_take(
                bool client,
                bool fromHost,
                bool inWorld)
            {
                var facts = Told with
                {
                    Client = client,
                    FromHost = fromHost,
                    InWorld = inWorld,
                };

                var news = OurDragRules.News(facts);

                news.ShouldBe(default(OwnerNews));
            }
        }

        public class LetsGo
        {
            [Fact]
            public void Forgets_our_own_entry_on_a_client()
            {
                OurDragRules.LetsGo(Ours).ShouldBe(LetGoEntry.Forget);
            }

            [Fact]
            public void Tells_everyone_on_the_host()
            {
                var owner = Ours with { Host = true };

                OurDragRules.LetsGo(owner).ShouldBe(LetGoEntry.Tell);
            }

            [Fact]
            public void Keeps_someone_elses_entry()
            {
                var owner = Ours with { Owner = 2, Host = true };

                OurDragRules.LetsGo(owner).ShouldBe(LetGoEntry.Keep);
            }

            [Fact]
            public void Keeps_the_table_when_nobody_drags_it()
            {
                var owner = Ours with { Owned = false };

                OurDragRules.LetsGo(owner).ShouldBe(LetGoEntry.Keep);
            }

            [Fact]
            public void Keeps_the_table_for_a_name_that_isnt_valid()
            {
                var owner = Ours with { Named = false };

                OurDragRules.LetsGo(owner).ShouldBe(LetGoEntry.Keep);
            }
        }

        public class KeepsOursHeard
        {
            [Fact]
            public void Keeps_our_own_drag_heard_on_the_host()
            {
                var owner = Ours with { Host = true };

                OurDragRules.KeepsOursHeard(owner).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_our_entry_on_a_client()
            {
                OurDragRules.KeepsOursHeard(Ours).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_someone_elses_entry()
            {
                var owner = Ours with { Owner = 2, Host = true };

                OurDragRules.KeepsOursHeard(owner).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_thing_with_no_entry()
            {
                var owner = Ours with { Owned = false, Host = true };

                OurDragRules.KeepsOursHeard(owner).ShouldBeFalse();
            }

            [Fact]
            public void Keeps_it_heard_for_a_name_that_isnt_valid()
            {
                var owner = Ours with { Named = false, Host = true };

                OurDragRules.KeepsOursHeard(owner).ShouldBeTrue();
            }
        }

        public class Hold
        {
            [Theory]
            [InlineData(false, false, false, HoldAt.Here)]
            [InlineData(false, false, true, HoldAt.Known)]
            [InlineData(false, true, true, HoldAt.HostWord)]
            [InlineData(false, true, false, HoldAt.HostWord)]
            [InlineData(true, true, true, HoldAt.None)]
            public void Holds_at_the_best_place_known(
                bool shown,
                bool hostWord,
                bool known,
                HoldAt at)
            {
                var hold = new HoldFacts
                {
                    Shown = shown,
                    HostWord = hostWord,
                    Known = known,
                };

                OurDragRules.Hold(hold).ShouldBe(at);
            }
        }

        public class SendDue
        {
            [Fact]
            public void Sends_an_agreed_drag_exactly_when_due()
            {
                OurDragRules.SendDue(Send).ShouldBeTrue();
            }

            [Fact]
            public void Waits_just_before_it_is_due()
            {
                var send = Send with { Now = new Seconds(9.99f) };

                OurDragRules.SendDue(send).ShouldBeFalse();
            }

            [Fact]
            public void Waits_for_the_hosts_agreement_on_a_client()
            {
                var send = Send with { Claimed = false };

                OurDragRules.SendDue(send).ShouldBeFalse();
            }

            [Fact]
            public void Sends_the_hosts_own_drag_unclaimed()
            {
                var send = Send with { Claimed = false, Host = true };

                OurDragRules.SendDue(send).ShouldBeTrue();
            }

            [Fact]
            public void Sends_nothing_for_a_name_that_isnt_valid()
            {
                var send = Send with { Named = false };

                OurDragRules.SendDue(send).ShouldBeFalse();
            }

            [Fact]
            public void Sends_nothing_without_a_drag()
            {
                var send = Send with { Dragging = false };

                OurDragRules.SendDue(send).ShouldBeFalse();
            }
        }

        public class NextSend
        {
            [Fact]
            public void Waits_a_tenth_of_a_second()
            {
                var next = OurDragRules.NextSend(new Seconds(10f));

                next.Value.ShouldBe(10.1f, 0.0001f);
            }
        }

        public class SettleAt
        {
            [Fact]
            public void Waits_a_second_and_a_half()
            {
                var at = OurDragRules.SettleAt(new Seconds(10f));

                at.Value.ShouldBe(11.5f);
            }
        }
    }
}
