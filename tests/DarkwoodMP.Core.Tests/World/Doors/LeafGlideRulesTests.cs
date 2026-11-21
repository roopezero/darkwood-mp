using System;
using DarkwoodMP.World;
using DarkwoodMP.World.Doors;

namespace DarkwoodMP.Tests.World.Doors
{
    public class LeafGlideRulesTests
    {
        static Seconds At(float t) => new Seconds(t);

        static readonly LeafDoorFacts Open = new LeafDoorFacts
        {
            Exists = true,
            HasBody = true,
            Opened = true,
        };

        /// The host hears a client push an open door's leaf to 30, from
        /// beside it.
        static readonly LeafHeardFacts Pushed = new LeafHeardFacts
        {
            InWorld = true,
            Host = true,
            Angle = 30f,
            Door = Open,
            Pusher = new ReachFacts
            {
                Known = true,
                StandIn = new Vec2(0f, 0f),
                Target = new Vec2(100f, 0f),
            },
            MaxTurn = 110f,
        };

        /// A client hears the host's angle for an open door.
        static readonly LeafHeardFacts FromHost = Pushed with
        {
            Host = false,
            Client = true,
            FromHost = true,
        };

        /// A leaf at 0 gliding to 20, the angle heard at time 0.
        static readonly GlideFacts Gliding = new GlideFacts
        {
            Door = Open,
            Angle = 0f,
            Target = 20f,
            Heard = At(0f),
            Now = At(0.125f),
            Delta = At(0.0625f),
        };

        static LeafHeardFacts PushedFrom(float x)
        {
            var pusher = Pushed.Pusher with { StandIn = new Vec2(x, 0f) };
            return Pushed with { Pusher = pusher };
        }

        public class Limits
        {
            [Fact]
            public void Pins_the_push_limits()
            {
                var limits = new[]
                {
                    LeafGlideRules.Reach,
                    LeafGlideRules.PushSlack,
                };

                limits.ShouldBe(new[] { 600f, 5f });
            }

            [Fact]
            public void Pins_the_glide()
            {
                var glide = new[]
                {
                    LeafGlideRules.CloseEnough,
                    LeafGlideRules.Rate,
                    LeafGlideRules.GlideRest.Value,
                    LeafGlideRules.GlideMax.Value,
                };

                glide.ShouldBe(new[] { 0.5f, 15f, 0.25f, 1f });
            }
        }

        public class Hear
        {
            [Fact]
            public void Relays_a_push_on_the_host()
            {
                var answer = LeafGlideRules.Hear(Pushed);

                answer.ShouldBe(LeafAnswer.Relay);
            }

            [Fact]
            public void Takes_the_hosts_angle_on_a_client()
            {
                var answer = LeafGlideRules.Hear(FromHost);

                answer.ShouldBe(LeafAnswer.Take);
            }

            [Fact]
            public void Drops_an_angle_with_no_world()
            {
                var heard = Pushed with { InWorld = false };

                var answer = LeafGlideRules.Hear(heard);

                answer.ShouldBe(LeafAnswer.Dropped);
            }

            [Fact]
            public void Drops_a_clients_angle_not_from_the_host()
            {
                var heard = FromHost with { FromHost = false };

                var answer = LeafGlideRules.Hear(heard);

                answer.ShouldBe(LeafAnswer.Dropped);
            }

            [Theory]
            [InlineData(float.NaN)]
            [InlineData(float.PositiveInfinity)]
            [InlineData(float.NegativeInfinity)]
            public void Drops_an_angle_that_isnt_a_number(float angle)
            {
                var heard = Pushed with { Angle = angle };

                var answer = LeafGlideRules.Hear(heard);

                answer.ShouldBe(LeafAnswer.Dropped);
            }

            [Fact]
            public void Finds_no_door_that_isnt_here()
            {
                var door = Open with { Exists = false };
                var heard = Pushed with { Door = door };

                var answer = LeafGlideRules.Hear(heard);

                answer.ShouldBe(LeafAnswer.NotFound);
            }

            [Fact]
            public void Finds_no_door_without_its_leaf()
            {
                var door = Open with { HasBody = false };
                var heard = Pushed with { Door = door };

                var answer = LeafGlideRules.Hear(heard);

                answer.ShouldBe(LeafAnswer.NotFound);
            }

            [Theory]
            [InlineData(0)]
            [InlineData(1)]
            [InlineData(2)]
            public void Leaves_a_door_not_open_here(int fact)
            {
                var heard = Pushed with { Door = NotOpen(fact) };

                var answer = LeafGlideRules.Hear(heard);

                answer.ShouldBe(LeafAnswer.NotOpen);
            }

            [Fact]
            public void Relays_a_push_exactly_at_the_reach()
            {
                var answer = LeafGlideRules.Hear(PushedFrom(-500f));

                answer.ShouldBe(LeafAnswer.Relay);
            }

            [Fact]
            public void Puts_back_a_push_just_past_the_reach()
            {
                var answer = LeafGlideRules.Hear(PushedFrom(-500.5f));

                answer.ShouldBe(LeafAnswer.PutBack);
            }

            [Fact]
            public void Relays_a_push_from_a_player_with_no_stand_in()
            {
                var far = PushedFrom(-4000f);
                var unknown = far.Pusher with { Known = false };
                var heard = far with { Pusher = unknown };

                var answer = LeafGlideRules.Hear(heard);

                answer.ShouldBe(LeafAnswer.Relay);
            }

            [Theory]
            [InlineData(115f, LeafAnswer.Relay)]
            [InlineData(115.5f, LeafAnswer.PutBack)]
            [InlineData(-115f, LeafAnswer.Relay)]
            [InlineData(-115.5f, LeafAnswer.PutBack)]
            public void Puts_back_an_angle_past_the_max_turn(
                float angle,
                LeafAnswer expected)
            {
                var heard = Pushed with { Angle = angle };

                var answer = LeafGlideRules.Hear(heard);

                answer.ShouldBe(expected);
            }

            [Fact]
            public void Takes_any_angle_on_a_client()
            {
                var heard = FromHost with { Angle = 170f, MaxTurn = 0f };

                var answer = LeafGlideRules.Hear(heard);

                answer.ShouldBe(LeafAnswer.Take);
            }

            [Fact]
            public void Prefers_a_bad_number_to_a_missing_door()
            {
                var door = Open with { Exists = false };
                var heard = Pushed with { Angle = float.NaN, Door = door };

                var answer = LeafGlideRules.Hear(heard);

                answer.ShouldBe(LeafAnswer.Dropped);
            }

            [Fact]
            public void Prefers_a_missing_door_to_a_shut_one()
            {
                var door = Open with { Exists = false, Opened = false };
                var heard = Pushed with { Door = door };

                var answer = LeafGlideRules.Hear(heard);

                answer.ShouldBe(LeafAnswer.NotFound);
            }

            [Fact]
            public void Prefers_a_shut_door_to_a_far_pusher()
            {
                var far = PushedFrom(-4000f);
                var heard = far with { Door = Open with { Opened = false } };

                var answer = LeafGlideRules.Hear(heard);

                answer.ShouldBe(LeafAnswer.NotOpen);
            }

            static LeafDoorFacts NotOpen(int fact)
            {
                switch (fact)
                {
                    case 0:
                        return Open with { Opened = false };
                    case 1:
                        return Open with { Destroyed = true };
                    default:
                        return Open with { Barricaded = true };
                }
            }
        }

        public class Glide
        {
            [Fact]
            public void Turns_a_leaf_far_from_the_target()
            {
                var step = LeafGlideRules.Glide(Gliding);

                step.Outcome.ShouldBe(GlideOutcome.Turn);
            }

            [Fact]
            public void Turns_the_leaf_a_step_nearer()
            {
                float k = 1f - (float)Math.Exp(-15f * 0.0625f);

                var step = LeafGlideRules.Glide(Gliding);

                step.Angle.ShouldBe(20f * k, 1e-4f);
            }

            [Theory]
            [InlineData(0)]
            [InlineData(1)]
            [InlineData(2)]
            [InlineData(3)]
            public void Stops_on_a_door_it_cant_turn(int fact)
            {
                var glide = Gliding with { Door = Without(fact) };

                var step = LeafGlideRules.Glide(glide);

                step.Outcome.ShouldBe(GlideOutcome.Stop);
            }

            [Fact]
            public void Settles_a_leaf_near_the_target_after_the_rest()
            {
                var glide = Gliding with { Angle = 19.75f, Now = At(0.5f) };

                var step = LeafGlideRules.Glide(glide);

                step.ShouldBe(new GlideStep
                {
                    Outcome = GlideOutcome.Settle,
                    Angle = 20f,
                });
            }

            [Fact]
            public void Turns_a_leaf_near_the_target_exactly_at_the_rest()
            {
                var glide = Gliding with { Angle = 19.75f, Now = At(0.25f) };

                var step = LeafGlideRules.Glide(glide);

                step.Outcome.ShouldBe(GlideOutcome.Turn);
            }

            [Fact]
            public void Turns_a_leaf_exactly_at_close_enough()
            {
                var glide = Gliding with { Angle = 19.5f, Now = At(0.5f) };

                var step = LeafGlideRules.Glide(glide);

                step.Outcome.ShouldBe(GlideOutcome.Turn);
            }

            [Fact]
            public void Turns_a_far_leaf_exactly_at_the_longest()
            {
                var glide = Gliding with { Now = At(1f) };

                var step = LeafGlideRules.Glide(glide);

                step.Outcome.ShouldBe(GlideOutcome.Turn);
            }

            [Fact]
            public void Settles_a_far_leaf_just_past_the_longest()
            {
                var glide = Gliding with { Now = At(1.0078125f) };

                var step = LeafGlideRules.Glide(glide);

                step.Outcome.ShouldBe(GlideOutcome.Settle);
            }

            [Fact]
            public void Turns_the_short_way_round()
            {
                var glide = Gliding with { Angle = 170f, Target = -170f };

                var step = LeafGlideRules.Glide(glide);

                step.Angle.ShouldBeGreaterThan(170f);
            }

            static LeafDoorFacts Without(int fact)
            {
                switch (fact)
                {
                    case 0:
                        return Open with { Exists = false };
                    case 1:
                        return Open with { HasBody = false };
                    case 2:
                        return Open with { Opened = false };
                    default:
                        return Open with { Destroyed = true };
                }
            }
        }

        public class Factor
        {
            [Fact]
            public void Turns_nothing_in_no_time()
            {
                var k = LeafGlideRules.Factor(At(0f));

                k.ShouldBe(0f);
            }

            [Fact]
            public void Closes_most_of_the_way_in_a_fifteenth_of_a_second()
            {
                var k = LeafGlideRules.Factor(At(1f / 15f));

                k.ShouldBe(1f - (float)Math.Exp(-1.0), 1e-4f);
            }
        }
    }
}
