using DarkwoodMP.World.Doors;

namespace DarkwoodMP.Tests.World.Doors
{
    public partial class LeafWatchRulesTests
    {
        public class Step
        {
            [Fact]
            public void Leaves_a_still_leaf_alone()
            {
                var step = LeafWatchRules.Step(Still);

                step.ShouldBe(new LeafStep { Next = Still.Watch });
            }

            [Fact]
            public void Follows_a_leaf_turned_past_the_change()
            {
                var frame = Still with { Angle = 4.5f };

                var step = LeafWatchRules.Step(frame);

                step.Next.Moving.ShouldBeTrue();
            }

            [Fact]
            public void Ignores_a_turn_exactly_at_the_change()
            {
                var frame = Still with { Angle = 4f };

                var step = LeafWatchRules.Step(frame);

                step.Next.Moving.ShouldBeFalse();
            }

            [Fact]
            public void Follows_a_leaf_turning_faster_than_a_swing()
            {
                var frame = Still with { Spin = 0.25f };

                var step = LeafWatchRules.Step(frame);

                step.Next.Moving.ShouldBeTrue();
            }

            [Fact]
            public void Ignores_a_spin_exactly_at_a_swing()
            {
                var frame = Still with { Spin = -0.2f };

                var step = LeafWatchRules.Step(frame);

                step.Next.Moving.ShouldBeFalse();
            }

            [Fact]
            public void Counts_a_player_just_inside_the_touch()
            {
                var frame = Still with
                {
                    Angle = 20f,
                    Player = new Vec2(119.5f, 0f),
                };

                var step = LeafWatchRules.Step(frame);

                step.Next.Touched.ShouldBeTrue();
            }

            [Fact]
            public void Ignores_a_player_exactly_at_the_touch()
            {
                var frame = Still with
                {
                    Angle = 20f,
                    Player = new Vec2(120f, 0f),
                };

                var step = LeafWatchRules.Step(frame);

                step.Next.Touched.ShouldBeFalse();
            }

            [Fact]
            public void Counts_the_tests_touch_wherever_the_player_is()
            {
                var frame = Still with { Angle = 20f, TestTouch = true };

                var step = LeafWatchRules.Step(frame);

                step.Next.Touched.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_a_slow_leaf_untouched_once_followed()
            {
                var frame = Still with
                {
                    Watch = new LeafWatch { Moving = true },
                    Angle = 20f,
                    TestTouch = true,
                };

                var step = LeafWatchRules.Step(frame);

                step.Next.Touched.ShouldBeFalse();
            }

            [Fact]
            public void Streams_a_swinging_leaf_our_player_pushed()
            {
                var step = LeafWatchRules.Step(Swinging);

                step.Move.ShouldBe(LeafMove.Stream);
            }

            [Fact]
            public void Streams_the_angle_it_is_at()
            {
                var step = LeafWatchRules.Step(Swinging);

                step.Angle.ShouldBe(30f);
            }

            [Fact]
            public void Streams_again_after_the_stream_interval()
            {
                var step = LeafWatchRules.Step(Swinging);

                step.Next.NextStream.Value.ShouldBe(10.1f, 1e-4f);
            }

            [Fact]
            public void Streams_exactly_when_due()
            {
                var watch = Swinging.Watch with { NextStream = At(10f) };

                var step = LeafWatchRules.Step(Swinging with { Watch = watch });

                step.Move.ShouldBe(LeafMove.Stream);
            }

            [Fact]
            public void Holds_a_stream_until_due()
            {
                var watch = Swinging.Watch with { NextStream = At(10.5f) };

                var step = LeafWatchRules.Step(Swinging with { Watch = watch });

                step.Move.ShouldBe(LeafMove.None);
            }

            [Fact]
            public void Streams_any_swing_from_the_host()
            {
                var watch = Swinging.Watch with { Touched = false };
                var frame = Swinging with { Watch = watch, Host = true };

                var step = LeafWatchRules.Step(frame);

                step.Move.ShouldBe(LeafMove.Stream);
            }

            [Fact]
            public void Keeps_a_swing_our_player_didnt_push_on_a_client()
            {
                var watch = Swinging.Watch with { Touched = false };

                var step = LeafWatchRules.Step(Swinging with { Watch = watch });

                step.Move.ShouldBe(LeafMove.None);
            }

            [Fact]
            public void Counts_a_stream_of_an_unnamed_door_as_sent()
            {
                var frame = Swinging with { Named = false };

                var step = LeafWatchRules.Step(frame);

                step.ShouldBe(new LeafStep
                {
                    Next = frame.Watch with { NextStream = At(10f + 0.1f) },
                });
            }

            [Fact]
            public void Stops_resting_while_it_swings()
            {
                var frame = Rested with { Spin = 1f };

                var step = LeafWatchRules.Step(frame);

                step.Next.Resting.ShouldBeFalse();
            }

            [Fact]
            public void Starts_resting_when_it_stops_swinging()
            {
                var frame = Swinging with { Spin = 0f };

                var step = LeafWatchRules.Step(frame);

                step.ShouldBe(new LeafStep
                {
                    Next = frame.Watch with
                    {
                        Resting = true,
                        RestingSince = At(10f),
                    },
                });
            }

            [Fact]
            public void Sends_where_it_rests_exactly_at_the_rest_time()
            {
                var frame = Rested with { Now = At(0.4f) };

                var step = LeafWatchRules.Step(frame);

                step.Move.ShouldBe(LeafMove.Send);
            }

            [Fact]
            public void Waits_for_the_rest_time()
            {
                var frame = Rested with { Now = At(0.39f) };

                var step = LeafWatchRules.Step(frame);

                step.ShouldBe(new LeafStep { Next = frame.Watch });
            }

            [Fact]
            public void Sends_exactly_at_the_next_send()
            {
                var watch = Rested.Watch with { NextSend = At(1f) };

                var step = LeafWatchRules.Step(Rested with { Watch = watch });

                step.Move.ShouldBe(LeafMove.Send);
            }

            [Fact]
            public void Waits_for_the_next_send()
            {
                var watch = Rested.Watch with { NextSend = At(1.5f) };

                var step = LeafWatchRules.Step(Rested with { Watch = watch });

                step.Move.ShouldBe(LeafMove.None);
            }

            [Fact]
            public void Sends_and_shares_where_our_push_rests()
            {
                var step = LeafWatchRules.Step(Rested);

                step.ShouldBe(new LeafStep
                {
                    Next = Rested.Watch with
                    {
                        Shared = 20f,
                        Moving = false,
                        Touched = false,
                        NextSend = At(1.5f),
                    },
                    Move = LeafMove.Send,
                    Angle = 20f,
                });
            }

            [Fact]
            public void Sends_where_any_leaf_rests_from_the_host()
            {
                var watch = Rested.Watch with { Touched = false };
                var frame = Rested with { Watch = watch, Host = true };

                var step = LeafWatchRules.Step(frame);

                step.Move.ShouldBe(LeafMove.Send);
            }

            [Fact]
            public void Swings_back_a_leaf_our_player_didnt_push()
            {
                var watch = Rested.Watch with { Touched = false };

                var step = LeafWatchRules.Step(Rested with { Watch = watch });

                step.ShouldBe(new LeafStep
                {
                    Next = watch with { Moving = false },
                    Move = LeafMove.SwingBack,
                    Angle = 0f,
                });
            }

            [Fact]
            public void Stops_following_a_leaf_back_where_it_was()
            {
                var frame = Rested with { Angle = 4f };

                var step = LeafWatchRules.Step(frame);

                step.ShouldBe(new LeafStep
                {
                    Next = frame.Watch with { Moving = false, Touched = false },
                });
            }

            [Fact]
            public void Sends_nothing_for_an_unnamed_door()
            {
                var frame = Rested with { Named = false };

                var step = LeafWatchRules.Step(frame);

                step.ShouldBe(new LeafStep
                {
                    Next = frame.Watch with { Moving = false, Touched = false },
                });
            }
        }
    }
}
