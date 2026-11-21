using DarkwoodMP.World.Dragging;

namespace DarkwoodMP.Tests.World.Dragging
{
    public class GlideRulesTests
    {
        /// A rest heard just now, 50 units away.
        static readonly GlideFacts Rest = new GlideFacts
        {
            Off = 50f,
            Turn = 10f,
            Last = true,
            Heard = new Seconds(0f),
            Now = new Seconds(0f),
        };

        /// A slide heard just now, 50 units away.
        static readonly GlideFacts Slide = Rest with { Last = false };

        public class Limits
        {
            [Fact]
            public void Pins_the_glide_limits()
            {
                var limits = new[]
                {
                    GlideRules.GlideFor,
                    GlideRules.GlideMax,
                    GlideRules.ThereWithin,
                    GlideRules.ThereTurn,
                    GlideRules.Rate,
                };

                limits.ShouldBe(new[] { 0.6f, 300f, 0.5f, 1f, 15f });
            }
        }

        public class Step
        {
            [Fact]
            public void Glides_toward_a_slide()
            {
                GlideRules.Step(Slide).ShouldBe(GlideAction.Glide);
            }

            [Fact]
            public void Glides_toward_a_slide_that_is_there()
            {
                var glide = Slide with { Off = 0f, Turn = 0f };

                GlideRules.Step(glide).ShouldBe(GlideAction.Glide);
            }

            [Fact]
            public void Glides_toward_a_slide_heard_exactly_at_stale()
            {
                var glide = Slide with { Now = new Seconds(3f) };

                GlideRules.Step(glide).ShouldBe(GlideAction.Glide);
            }

            [Fact]
            public void Lets_go_of_a_slide_heard_past_stale()
            {
                var glide = Slide with { Now = new Seconds(3.01f) };

                GlideRules.Step(glide).ShouldBe(GlideAction.Release);
            }

            [Fact]
            public void Glides_to_a_fresh_rest()
            {
                GlideRules.Step(Rest).ShouldBe(GlideAction.Glide);
            }

            [Fact]
            public void Lets_go_of_a_rest_that_is_there()
            {
                var glide = Rest with { Off = 0.49f, Turn = 0.99f };

                GlideRules.Step(glide).ShouldBe(GlideAction.Release);
            }

            [Fact]
            public void Glides_to_a_rest_exactly_half_a_unit_away()
            {
                var glide = Rest with { Off = 0.5f, Turn = 0f };

                GlideRules.Step(glide).ShouldBe(GlideAction.Glide);
            }

            [Fact]
            public void Glides_to_a_rest_turned_exactly_a_degree()
            {
                var glide = Rest with { Off = 0f, Turn = 1f };

                GlideRules.Step(glide).ShouldBe(GlideAction.Glide);
            }

            [Fact]
            public void Glides_to_a_rest_just_under_the_glide_max()
            {
                var glide = Rest with { Off = 299.9f };

                GlideRules.Step(glide).ShouldBe(GlideAction.Glide);
            }

            [Fact]
            public void Puts_a_rest_exactly_at_the_glide_max_there_at_once()
            {
                var glide = Rest with { Off = 300f };

                GlideRules.Step(glide).ShouldBe(GlideAction.Put);
            }

            [Fact]
            public void Puts_a_rest_heard_exactly_glide_for_ago_there()
            {
                var glide = Rest with { Now = new Seconds(0.6f) };

                GlideRules.Step(glide).ShouldBe(GlideAction.Put);
            }

            [Fact]
            public void Lets_go_of_a_rest_heard_past_glide_for()
            {
                var glide = Rest with { Now = new Seconds(0.61f) };

                GlideRules.Step(glide).ShouldBe(GlideAction.Release);
            }

            [Fact]
            public void Lets_go_of_a_far_rest_that_is_stale()
            {
                var glide = Rest with { Off = 500f, Now = new Seconds(3.01f) };

                GlideRules.Step(glide).ShouldBe(GlideAction.Release);
            }
        }

        public class Blend
        {
            [Fact]
            public void Closes_nothing_in_no_time()
            {
                GlideRules.Blend(new Seconds(0f)).ShouldBe(0f);
            }

            [Fact]
            public void Closes_most_of_the_gap_in_a_tenth_of_a_second()
            {
                var k = GlideRules.Blend(new Seconds(0.1f));

                k.ShouldBe(0.7769f, 0.0001f);
            }
        }
    }
}
