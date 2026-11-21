using DarkwoodMP.World.Dragging;

namespace DarkwoodMP.Tests.World.Dragging
{
    /// The tests of PutsAtHost and Joiner are in PushRulesTests.Place.cs.
    public partial class PushRulesTests
    {
        /// A crate with a body, free to move.
        static readonly LooseFacts Crate = new LooseFacts
        {
            Body = true,
            Item = true,
        };

        /// On the host: a thing first seen 20 units away, still sliding.
        static readonly PushFacts Sliding = new PushFacts
        {
            Moved = 20f,
            Host = true,
            HostRunsHere = true,
            Collider = true,
            Reach = 1000000f,
            Now = new Seconds(0f),
        };

        /// On the host: a thing away since 0, come to rest at 5.
        static readonly PushFacts Rested = Sliding with
        {
            Strayed = true,
            Since = new Seconds(0f),
            AtRest = true,
            Now = new Seconds(5f),
        };

        /// On a client, in an area the host runs: a thing our player isn't
        /// at, away since 0 and come to rest at 5.
        static readonly PushFacts Nudged = Rested with { Host = false };

        /// Client: our copy of a loose thing, 10 units from the host's.
        static readonly PlaceFacts Copy = new PlaceFacts
        {
            Client = true,
            Loose = true,
            Distance = 10f,
        };

        /// Host: a loose thing at rest 10 units from its snapshot place.
        static readonly SnapshotFacts Moved = new SnapshotFacts
        {
            Moved = 10f,
            Loose = true,
            AtRest = true,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_watch_limits()
            {
                var limits = new[]
                {
                    PushRules.WatchRadius,
                    PushRules.WatchEvery,
                    PushRules.LookEvery,
                    PushRules.MinMove,
                    PushRules.MinTurn,
                    PushRules.Touch,
                    PushRules.GiveUpAfter,
                    PushRules.HitFor,
                    PushRules.RestSpeed,
                    PushRules.RestSpin,
                };

                limits.ShouldBe(new[]
                {
                    600f, 0.1f, 0.25f, 0.5f, 2f, 120f, 10f, 3f, 1f, 0.01f,
                });
            }

            [Fact]
            public void Pins_the_most_colliders_a_look_takes()
            {
                PushRules.LookMost.ShouldBe(512);
            }
        }

        public class IsLoose
        {
            [Fact]
            public void Takes_an_item_free_to_move()
            {
                PushRules.IsLoose(Crate).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_out_no_body()
            {
                var body = Crate with { Body = false };

                PushRules.IsLoose(body).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_frozen_body()
            {
                var body = Crate with { Frozen = true };

                PushRules.IsLoose(body).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_body_with_no_item()
            {
                var body = Crate with { Item = false };

                PushRules.IsLoose(body).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_character()
            {
                var body = Crate with { Character = true };

                PushRules.IsLoose(body).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_part_of_a_door()
            {
                var body = Crate with { InDoor = true };

                PushRules.IsLoose(body).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_part_of_a_window()
            {
                var body = Crate with { InWindow = true };

                PushRules.IsLoose(body).ShouldBeFalse();
            }
        }

        public class AtRest
        {
            [Theory]
            [InlineData(true, 100f, 100f, true)]
            [InlineData(false, 0.99f, 0.0099f, true)]
            [InlineData(false, 1f, 0f, false)]
            [InlineData(false, 0f, 0.01f, false)]
            public void Rests_asleep_or_under_both_limits(
                bool asleep,
                float speed,
                float spin,
                bool rests)
            {
                PushRules.AtRest(asleep, speed, spin).ShouldBe(rests);
            }
        }

        public class Away
        {
            [Theory]
            [InlineData(0.49f, 1.99f, false)]
            [InlineData(0.5f, 0f, true)]
            [InlineData(0f, 2f, true)]
            public void Is_away_from_half_a_unit_or_two_degrees(
                float moved,
                float turned,
                bool away)
            {
                PushRules.Away(moved, turned).ShouldBe(away);
            }
        }

        public class Watch
        {
            [Fact]
            public void Sends_a_slide_on_the_host()
            {
                var step = PushRules.Watch(Sliding);

                step.Outcome.ShouldBe(PushOutcome.Slide);
            }

            [Fact]
            public void Notes_when_it_was_first_seen_away()
            {
                var push = Sliding with { Now = new Seconds(7f) };

                var step = PushRules.Watch(push);

                step.Since.ShouldBe(new Seconds(7f));
            }

            [Fact]
            public void Keeps_when_it_was_first_seen_away()
            {
                var push = Sliding with
                {
                    Strayed = true,
                    Since = new Seconds(2f),
                    Now = new Seconds(7f),
                };

                var step = PushRules.Watch(push);

                step.Since.ShouldBe(new Seconds(2f));
            }

            [Fact]
            public void Settles_a_rest_on_the_host()
            {
                var step = PushRules.Watch(Rested);

                step.ShouldBe(new PushStep { Outcome = PushOutcome.Settle });
            }

            [Fact]
            public void Keeps_waiting_for_a_slide_just_under_give_up()
            {
                var push = Rested with
                {
                    AtRest = false,
                    Now = new Seconds(9.99f),
                };

                var step = PushRules.Watch(push);

                step.Outcome.ShouldBe(PushOutcome.Slide);
            }

            [Fact]
            public void Settles_a_slide_exactly_at_give_up()
            {
                var push = Rested with
                {
                    AtRest = false,
                    Now = new Seconds(10f),
                };

                var step = PushRules.Watch(push);

                step.Outcome.ShouldBe(PushOutcome.Settle);
            }

            [Fact]
            public void Drops_a_thing_that_is_gone()
            {
                var push = Rested with { Gone = true, Kinematic = true };

                var step = PushRules.Watch(push);

                step.Outcome.ShouldBe(PushOutcome.Drop);
            }

            [Theory]
            [InlineData(true, false, false, false)]
            [InlineData(false, true, false, false)]
            [InlineData(false, false, true, false)]
            [InlineData(false, false, false, true)]
            public void Rebases_a_held_thing(
                bool kinematic,
                bool dragged,
                bool settling,
                bool shown)
            {
                var push = Nudged with
                {
                    Kinematic = kinematic,
                    Dragged = dragged,
                    Settling = settling,
                    Shown = shown,
                    Touched = true,
                };

                var step = PushRules.Watch(push);

                step.ShouldBe(new PushStep { Outcome = PushOutcome.Rebase });
            }

            [Fact]
            public void Leaves_a_thing_that_barely_moved()
            {
                var push = Nudged with
                {
                    Moved = 0.49f,
                    Turned = 1.99f,
                    Touched = true,
                };

                var step = PushRules.Watch(push);

                step.ShouldBe(new PushStep { Outcome = PushOutcome.Still });
            }

            [Fact]
            public void Watches_a_thing_moved_exactly_half_a_unit()
            {
                var push = Nudged with { Moved = 0.5f };

                var step = PushRules.Watch(push);

                step.Outcome.ShouldBe(PushOutcome.PutBack);
            }

            [Fact]
            public void Watches_a_thing_turned_exactly_two_degrees()
            {
                var push = Nudged with { Moved = 0f, Turned = 2f };

                var step = PushRules.Watch(push);

                step.Outcome.ShouldBe(PushOutcome.PutBack);
            }

            [Fact]
            public void Puts_back_what_moved_by_itself_on_a_client()
            {
                var step = PushRules.Watch(Nudged);

                step.ShouldBe(new PushStep { Outcome = PushOutcome.PutBack });
            }

            [Fact]
            public void Waits_on_a_slide_it_didnt_push_on_a_client()
            {
                var push = Nudged with { AtRest = false };

                var step = PushRules.Watch(push);

                step.Outcome.ShouldBe(PushOutcome.Wait);
            }

            [Fact]
            public void Settles_our_players_push_on_a_client()
            {
                var push = Nudged with { Reach = 14399f };

                var step = PushRules.Watch(push);

                step.Outcome.ShouldBe(PushOutcome.Settle);
            }

            [Fact]
            public void Doesnt_count_a_player_exactly_at_touch()
            {
                var push = Nudged with { Reach = 14400f };

                var step = PushRules.Watch(push);

                step.Outcome.ShouldBe(PushOutcome.PutBack);
            }

            [Fact]
            public void Doesnt_count_a_player_near_a_thing_with_no_collider()
            {
                var push = Nudged with { Reach = 0f, Collider = false };

                var step = PushRules.Watch(push);

                step.Outcome.ShouldBe(PushOutcome.PutBack);
            }

            [Fact]
            public void Remembers_our_players_touch_while_it_slides()
            {
                var push = Nudged with { AtRest = false, Reach = 0f };

                var step = PushRules.Watch(push);

                step.Touched.ShouldBeTrue();
            }

            [Fact]
            public void Sends_our_players_slide_on_a_client()
            {
                var push = Nudged with { AtRest = false, Touched = true };

                var step = PushRules.Watch(push);

                step.Outcome.ShouldBe(PushOutcome.Slide);
            }

            [Fact]
            public void Counts_our_hit_just_under_three_seconds_ago()
            {
                var push = Nudged with
                {
                    Hit = true,
                    HitAt = new Seconds(2.01f),
                };

                var step = PushRules.Watch(push);

                step.Outcome.ShouldBe(PushOutcome.Settle);
            }

            [Fact]
            public void Doesnt_count_our_hit_exactly_three_seconds_ago()
            {
                var push = Nudged with
                {
                    Hit = true,
                    HitAt = new Seconds(2f),
                };

                var step = PushRules.Watch(push);

                step.Outcome.ShouldBe(PushOutcome.PutBack);
            }

            [Fact]
            public void Doesnt_count_a_hit_that_never_happened()
            {
                var push = Nudged with { HitAt = new Seconds(4f) };

                var step = PushRules.Watch(push);

                step.Outcome.ShouldBe(PushOutcome.PutBack);
            }

            [Fact]
            public void Doesnt_count_a_touch_on_the_host()
            {
                var push = Sliding with { Reach = 0f };

                var step = PushRules.Watch(push);

                step.Touched.ShouldBeFalse();
            }

            [Fact]
            public void Puts_back_our_players_push_where_the_host_isnt()
            {
                var push = Nudged with { HostRunsHere = false, Reach = 0f };

                var step = PushRules.Watch(push);

                step.Outcome.ShouldBe(PushOutcome.PutBack);
            }

            [Fact]
            public void Settles_a_push_touched_before_where_the_host_isnt()
            {
                var push = Nudged with
                {
                    HostRunsHere = false,
                    Touched = true,
                };

                var step = PushRules.Watch(push);

                step.Outcome.ShouldBe(PushOutcome.Settle);
            }

            [Fact]
            public void Forgets_the_touch_of_a_thing_back_at_its_place()
            {
                var push = Nudged with { Moved = 0f, Touched = true };

                var step = PushRules.Watch(push);

                step.Touched.ShouldBeFalse();
            }
        }
    }
}
