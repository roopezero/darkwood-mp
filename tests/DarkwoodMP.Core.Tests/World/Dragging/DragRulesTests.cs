using DarkwoodMP.World.Dragging;

namespace DarkwoodMP.Tests.World.Dragging
{
    /// The tests of TakeMove are in DragRulesTests.TakeMove.cs.
    public partial class DragRulesTests
    {
        /// Player 2 drags a named thing, heard just now; we are player 1,
        /// the host.
        static readonly OwnerFacts Dragged = new OwnerFacts
        {
            Named = true,
            Owned = true,
            Owner = 2,
            Local = 1,
            Heard = new Seconds(0f),
            Now = new Seconds(0f),
            Host = true,
        };

        /// Player 3 claims a thing in reach that nobody drags.
        static readonly ClaimFacts Claim = new ClaimFacts
        {
            Host = true,
            Handling = true,
            Found = true,
            Reach = Verdict.Allow,
            From = 3,
            Heard = new Seconds(0f),
            Now = new Seconds(0f),
        };

        /// Claim, while player 2 drags it, heard just now.
        static readonly ClaimFacts Busy = Claim with
        {
            Owned = true,
            Owner = 2,
            OwnerName = "Anna",
        };

        /// The reach's refusal of a box 700 away.
        static readonly Verdict Far = Verdict.Refuse(
            Refusal.OutOfReach,
            Refusals.Detail("700", "600", "Box"));

        /// Player 3 slides a thing nobody drags, in reach.
        static readonly DragMoveFacts Slide = new DragMoveFacts
        {
            From = 3,
            Found = true,
            NearThing = true,
            NearPlace = true,
            Heard = new Seconds(0f),
            Now = new Seconds(0f),
        };

        /// Player 3 moves the thing it drags, heard just now.
        static readonly DragMoveFacts OwnMove = Slide with
        {
            Owned = true,
            Owner = 3,
        };

        /// Player 3 moves the thing player 2 drags, heard just now.
        static readonly DragMoveFacts OtherMove = Slide with
        {
            Owned = true,
            Owner = 2,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_owner_times()
            {
                var limits = new[]
                {
                    DragRules.StaleAfter,
                    DragRules.ClientWait,
                    DragRules.CorrectEvery,
                    DragRules.SweepEvery,
                };

                limits.ShouldBe(new[] { 3f, 2f, 0.5f, 1f });
            }
        }

        public class IsStale
        {
            [Fact]
            public void Keeps_an_owner_on_the_host_heard_exactly_at_the_limit()
            {
                var owner = Dragged with { Now = new Seconds(3f) };

                DragRules.IsStale(owner).ShouldBeFalse();
            }

            [Fact]
            public void Lets_go_of_an_owner_on_the_host_heard_past_the_limit()
            {
                var owner = Dragged with { Now = new Seconds(3.01f) };

                DragRules.IsStale(owner).ShouldBeTrue();
            }

            [Fact]
            public void Waits_two_seconds_longer_on_a_client()
            {
                var owner = Dragged with
                {
                    Host = false,
                    Now = new Seconds(5f),
                };

                DragRules.IsStale(owner).ShouldBeFalse();
            }

            [Fact]
            public void Lets_go_on_a_client_past_five_seconds()
            {
                var owner = Dragged with
                {
                    Host = false,
                    Now = new Seconds(5.01f),
                };

                DragRules.IsStale(owner).ShouldBeTrue();
            }
        }

        public class OtherOwner
        {
            [Fact]
            public void Names_someone_else_who_drags_it()
            {
                DragRules.OtherOwner(Dragged).ShouldBe(2UL);
            }

            [Fact]
            public void Names_nobody_for_a_name_that_isnt_valid()
            {
                var owner = Dragged with { Named = false };

                DragRules.OtherOwner(owner).ShouldBe(0UL);
            }

            [Fact]
            public void Names_nobody_when_nobody_drags_it()
            {
                var owner = Dragged with { Owned = false };

                DragRules.OtherOwner(owner).ShouldBe(0UL);
            }

            [Fact]
            public void Names_nobody_for_our_own_drag()
            {
                var owner = Dragged with { Owner = 1 };

                DragRules.OtherOwner(owner).ShouldBe(0UL);
            }

            [Fact]
            public void Names_nobody_for_a_stale_drag()
            {
                var owner = Dragged with { Now = new Seconds(3.01f) };

                DragRules.OtherOwner(owner).ShouldBe(0UL);
            }
        }

        public class MayClaim
        {
            [Fact]
            public void Agrees_to_a_thing_nobody_drags()
            {
                var claim = DragRules.MayClaim(Claim);

                claim.Answer.ShouldBe(ClaimAnswer.Agreed);
            }

            [Fact]
            public void Sends_nothing_back_when_it_agrees()
            {
                var claim = DragRules.MayClaim(Claim);

                claim.PutBack.ShouldBeFalse();
            }

            [Fact]
            public void Does_nothing_on_a_client()
            {
                var facts = Claim with { Host = false };

                var claim = DragRules.MayClaim(facts);

                claim.ShouldBe(default(ClaimOutcome));
            }

            [Fact]
            public void Does_nothing_outside_a_request()
            {
                var facts = Claim with { Handling = false };

                var claim = DragRules.MayClaim(facts);

                claim.Answer.ShouldBe(ClaimAnswer.None);
            }

            [Fact]
            public void Refuses_a_thing_that_isnt_here()
            {
                var facts = Claim with { Found = false };

                var claim = DragRules.MayClaim(facts);

                claim.Verdict.Reason.ShouldBe(Refusal.ItemNotHere);
            }

            [Fact]
            public void Sends_no_place_for_a_thing_that_isnt_here()
            {
                var facts = Claim with { Found = false };

                var claim = DragRules.MayClaim(facts);

                claim.PutBack.ShouldBeFalse();
            }

            [Fact]
            public void Words_a_thing_that_isnt_here_as_the_host_does()
            {
                var facts = Claim with { Found = false };

                var claim = DragRules.MayClaim(facts);

                Refusals.Text(claim.Verdict).ShouldBe("it isn't here");
            }

            [Fact]
            public void Refuses_with_the_reachs_own_verdict()
            {
                var facts = Claim with { Reach = Far };

                var claim = DragRules.MayClaim(facts);

                claim.Verdict.ShouldBe(Far);
            }

            [Fact]
            public void Puts_back_a_claim_out_of_reach()
            {
                var facts = Claim with { Reach = Far };

                var claim = DragRules.MayClaim(facts);

                claim.PutBack.ShouldBeTrue();
            }

            [Fact]
            public void Refuses_a_thing_someone_else_drags()
            {
                var claim = DragRules.MayClaim(Busy);

                claim.Verdict.Reason.ShouldBe(Refusal.OtherMoving);
            }

            [Fact]
            public void Puts_back_a_claim_on_someone_elses_drag()
            {
                var claim = DragRules.MayClaim(Busy);

                claim.PutBack.ShouldBeTrue();
            }

            [Fact]
            public void Names_the_owner_in_the_refusal()
            {
                var claim = DragRules.MayClaim(Busy);

                Refusals.Text(claim.Verdict).ShouldBe("Anna is moving it");
            }

            [Fact]
            public void Keeps_an_owner_heard_exactly_at_the_limit()
            {
                var facts = Busy with { Now = new Seconds(3f) };

                var claim = DragRules.MayClaim(facts);

                claim.Verdict.Reason.ShouldBe(Refusal.OtherMoving);
            }

            [Fact]
            public void Takes_over_a_drag_heard_past_the_limit()
            {
                var facts = Busy with { Now = new Seconds(3.01f) };

                var claim = DragRules.MayClaim(facts);

                claim.Answer.ShouldBe(ClaimAnswer.Agreed);
            }

            [Fact]
            public void Agrees_again_to_the_owner()
            {
                var facts = Busy with { Owner = 3 };

                var claim = DragRules.MayClaim(facts);

                claim.Answer.ShouldBe(ClaimAnswer.Agreed);
            }

            [Fact]
            public void Refuses_a_missing_thing_before_its_reach()
            {
                var facts = Busy with { Found = false, Reach = Far };

                var claim = DragRules.MayClaim(facts);

                claim.Verdict.Reason.ShouldBe(Refusal.ItemNotHere);
            }

            [Fact]
            public void Refuses_out_of_reach_before_someone_elses_drag()
            {
                var facts = Busy with { Reach = Far };

                var claim = DragRules.MayClaim(facts);

                claim.Verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }
        }

        public class Sweep
        {
            [Fact]
            public void Keeps_an_owner_heard_exactly_at_the_limit()
            {
                var owner = Dragged with { Now = new Seconds(3f) };

                DragRules.Sweep(owner).ShouldBe(OwnerSweep.Keep);
            }

            [Fact]
            public void Frees_a_stale_owner_on_the_host()
            {
                var owner = Dragged with { Now = new Seconds(3.01f) };

                DragRules.Sweep(owner).ShouldBe(OwnerSweep.Free);
            }

            [Fact]
            public void Forgets_a_stale_owner_on_a_client()
            {
                var owner = Dragged with
                {
                    Host = false,
                    Now = new Seconds(5.01f),
                };

                DragRules.Sweep(owner).ShouldBe(OwnerSweep.Forget);
            }

            [Fact]
            public void Frees_the_hosts_own_stale_drag()
            {
                var owner = Dragged with
                {
                    Owner = 1,
                    Now = new Seconds(3.01f),
                };

                DragRules.Sweep(owner).ShouldBe(OwnerSweep.Free);
            }
        }

        public class ForgetsCorrections
        {
            [Theory]
            [InlineData(0, true)]
            [InlineData(1, false)]
            public void Forgets_only_when_nobody_drags_anything(
                int owners,
                bool forgets)
            {
                DragRules.ForgetsCorrections(owners).ShouldBe(forgets);
            }
        }

        public class FreesOnLeave
        {
            [Fact]
            public void Frees_the_drag_of_the_player_who_left()
            {
                DragRules.FreesOnLeave(Dragged, 2).ShouldBeTrue();
            }

            [Fact]
            public void Keeps_someone_elses_drag()
            {
                DragRules.FreesOnLeave(Dragged, 3).ShouldBeFalse();
            }

            [Fact]
            public void Keeps_it_on_a_client()
            {
                var owner = Dragged with { Host = false };

                DragRules.FreesOnLeave(owner, 2).ShouldBeFalse();
            }
        }
    }
}
