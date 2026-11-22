using DarkwoodMP.World;

namespace DarkwoodMP.Tests.World
{
    public class ReachRulesTests
    {
        /// A request from a player whose stand-in is at the origin, about
        /// a door exactly at the reach.
        static readonly ReachFacts AtReach = new ReachFacts
        {
            Handling = true,
            Known = true,
            StandIn = new Vec2(0f, 0f),
            Target = new Vec2(600f, 0f),
            Reach = 600f,
            What = "Wooden door",
        };

        static readonly ReachFacts Far = AtReach with
        {
            Target = new Vec2(4041f, 0f),
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_shared_reach_limits()
            {
                var limits = new[] { ReachRules.Reach, ReachRules.ShotReach };

                limits.ShouldBe(new[] { 600f, 1200f });
            }
        }

        public class InReach
        {
            [Fact]
            public void Allows_a_thing_exactly_at_the_reach()
            {
                var verdict = ReachRules.InReach(AtReach);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_thing_just_past_the_reach()
            {
                var reach = AtReach with { Target = new Vec2(600.5f, 0f) };

                var verdict = ReachRules.InReach(reach);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Words_the_refusal_as_the_host_does()
            {
                var verdict = ReachRules.InReach(Far);

                var text = Refusals.Text(verdict);

                text.ShouldBe("Wooden door is 4041 away from them (reach 600)");
            }

            [Fact]
            public void Allows_anything_outside_a_request()
            {
                var reach = Far with { Handling = false };

                var verdict = ReachRules.InReach(reach);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Allows_a_requester_with_no_stand_in()
            {
                var reach = Far with { Known = false };

                var verdict = ReachRules.InReach(reach);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Takes_the_accepted_position_when_it_is_nearer()
            {
                var reach = Far with
                {
                    Checked = true,
                    Accepted = new Vec2(3500f, 0f),
                };

                var verdict = ReachRules.InReach(reach);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Names_the_nearer_distance_when_both_are_too_far()
            {
                var reach = Far with
                {
                    Checked = true,
                    Accepted = new Vec2(3000f, 0f),
                };

                var verdict = ReachRules.InReach(reach);

                Refusals.Text(verdict).ShouldStartWith("Wooden door is 1041 ");
            }

            [Fact]
            public void Keeps_the_stand_in_when_it_is_nearer()
            {
                var reach = Far with
                {
                    Checked = true,
                    Accepted = new Vec2(-1000f, 0f),
                };

                var verdict = ReachRules.InReach(reach);

                Refusals.Text(verdict).ShouldStartWith("Wooden door is 4041 ");
            }

            [Fact]
            public void Ignores_an_accepted_position_not_checked()
            {
                var reach = Far with { Accepted = new Vec2(4041f, 0f) };

                var verdict = ReachRules.InReach(reach);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }
        }

        public class Near
        {
            [Fact]
            public void Allows_a_player_with_no_stand_in()
            {
                var reach = Far with { Known = false };

                var near = ReachRules.Near(reach);

                near.ShouldBeTrue();
            }

            [Fact]
            public void Holds_exactly_at_the_reach()
            {
                var near = ReachRules.Near(AtReach);

                near.ShouldBeTrue();
            }

            [Fact]
            public void Fails_just_past_the_reach()
            {
                var reach = AtReach with { Target = new Vec2(600.5f, 0f) };

                var near = ReachRules.Near(reach);

                near.ShouldBeFalse();
            }

            [Fact]
            public void Ignores_the_request_and_the_accepted_position()
            {
                var reach = Far with
                {
                    Handling = false,
                    Checked = true,
                    Accepted = new Vec2(4041f, 0f),
                };

                var near = ReachRules.Near(reach);

                near.ShouldBeFalse();
            }
        }
    }
}
