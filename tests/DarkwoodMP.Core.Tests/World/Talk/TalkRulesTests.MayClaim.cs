using DarkwoodMP.World;
using DarkwoodMP.World.Talk;

namespace DarkwoodMP.Tests.World.Talk
{
    /// The host's check of a joiner's claim.
    public partial class TalkRulesTests
    {
        /// Peer 5 claims a free NPC that resolves, standing next to them,
        /// and nobody let go of them before.
        static readonly TalkFacts Free = new TalkFacts
        {
            From = 5,
            Now = new Seconds(100f),
            Resolved = true,
            Reach = new ReachFacts
            {
                Handling = true,
                Known = true,
                StandIn = new Vec2(0f, 0f),
                Target = new Vec2(1500f, 0f),
                What = "Doctor",
            },
        };

        public class MayClaim
        {
            [Fact]
            public void Gives_a_free_npc_exactly_at_the_reach()
            {
                var verdict = TalkRules.MayClaim(Free);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_an_npc_another_player_holds()
            {
                var claim = Free with
                {
                    Held = true,
                    Holder = 6,
                    HolderName = "Ann",
                };

                var verdict = TalkRules.MayClaim(claim);

                verdict.ShouldBe(Verdict.Refuse(Refusal.OtherTalking, "Ann"));
            }

            [Fact]
            public void Gives_the_requester_its_own_lock_again()
            {
                var claim = Free with { Held = true, Holder = 5 };

                var verdict = TalkRules.MayClaim(claim);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_claim_just_inside_the_grace()
            {
                var claim = Free with
                {
                    Released = true,
                    Releaser = 6,
                    ReleaserName = "Ann",
                    ReleasedAt = new Seconds(98.6f),
                };

                var verdict = TalkRules.MayClaim(claim);

                verdict.ShouldBe(Verdict.Refuse(Refusal.JustTalked, "Ann"));
            }

            [Fact]
            public void Words_the_grace_refusal_as_the_host_does()
            {
                var claim = Free with
                {
                    Released = true,
                    Releaser = 6,
                    ReleaserName = "Ann",
                    ReleasedAt = new Seconds(99f),
                };

                var text = Refusals.Text(TalkRules.MayClaim(claim));

                text.ShouldBe(
                    "Ann just talked to them: try again in a moment");
            }

            [Fact]
            public void Gives_it_exactly_at_the_end_of_the_grace()
            {
                var claim = Free with
                {
                    Released = true,
                    Releaser = 6,
                    ReleasedAt = new Seconds(98.5f),
                };

                var verdict = TalkRules.MayClaim(claim);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Gives_it_to_whoever_let_go_inside_the_grace()
            {
                var claim = Free with
                {
                    Released = true,
                    Releaser = 5,
                    ReleasedAt = new Seconds(100f),
                };

                var verdict = TalkRules.MayClaim(claim);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_an_npc_just_past_the_reach()
            {
                var claim = Free with
                {
                    Reach = Free.Reach with { Target = new Vec2(1501f, 0f) },
                };

                var verdict = TalkRules.MayClaim(claim);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Words_the_reach_refusal_as_the_host_does()
            {
                var claim = Free with
                {
                    Reach = Free.Reach with { Target = new Vec2(2000f, 0f) },
                };

                var text = Refusals.Text(TalkRules.MayClaim(claim));

                text.ShouldBe("Doctor is 2000 away from them (reach 1500)");
            }

            [Fact]
            public void Uses_its_own_reach_whatever_the_facts_hold()
            {
                var claim = Free with
                {
                    Reach = Free.Reach with { Reach = 600f },
                };

                var verdict = TalkRules.MayClaim(claim);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Skips_the_reach_of_an_npc_the_host_cannot_find()
            {
                var claim = Free with
                {
                    Resolved = false,
                    Reach = Free.Reach with { Target = new Vec2(9000f, 0f) },
                };

                var verdict = TalkRules.MayClaim(claim);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Skips_the_reach_of_a_requester_with_no_stand_in()
            {
                var claim = Free with
                {
                    Reach = Free.Reach with
                    {
                        Known = false,
                        Target = new Vec2(9000f, 0f),
                    },
                };

                var verdict = TalkRules.MayClaim(claim);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Gives_a_bare_claim_with_no_reach_check()
            {
                var claim = Free with
                {
                    Reach = Free.Reach with
                    {
                        Handling = false,
                        Target = new Vec2(9000f, 0f),
                    },
                };

                var verdict = TalkRules.MayClaim(claim);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_held_npc_before_the_grace()
            {
                var claim = Free with
                {
                    Held = true,
                    Holder = 6,
                    Released = true,
                    Releaser = 7,
                    ReleasedAt = new Seconds(100f),
                };

                var verdict = TalkRules.MayClaim(claim);

                verdict.Reason.ShouldBe(Refusal.OtherTalking);
            }

            [Fact]
            public void Refuses_inside_the_grace_before_the_reach()
            {
                var claim = Free with
                {
                    Released = true,
                    Releaser = 7,
                    ReleasedAt = new Seconds(100f),
                    Reach = Free.Reach with { Target = new Vec2(9000f, 0f) },
                };

                var verdict = TalkRules.MayClaim(claim);

                verdict.Reason.ShouldBe(Refusal.JustTalked);
            }
        }
    }
}
