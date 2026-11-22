using DarkwoodMP.World.Npcs;

namespace DarkwoodMP.Tests.World.Npcs
{
    public class NpcPlaceRulesTests
    {
        /// A live NPC nobody else runs.
        static readonly NpcFacts Doctor = new NpcFacts
        {
            HasCharacter = true,
            Alive = true,
        };

        /// An NPC sent before at the origin, now exactly Moved away.
        static readonly NpcSendFacts AtMoved = new NpcSendFacts
        {
            Sent = true,
            Was = new Vec2(0f, 0f),
            At = new Vec2(30f, 0f),
        };

        /// A client's copy 100 off the host's, a frame of 0.1 s.
        static readonly NpcFollowFacts Off = new NpcFollowFacts
        {
            HasObject = true,
            Movable = true,
            At = new Vec3(0f, 5f, 0f),
            Target = new Vec2(100f, 0f),
            Delta = new Seconds(0.1f),
        };

        /// Timers due at 10 s for the moved send and at 12 s for all.
        static readonly NpcTimers Timers = new NpcTimers
        {
            NextMoved = new Seconds(10f),
            NextAll = new Seconds(12f),
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_placement_limits()
            {
                var limits = new[]
                {
                    NpcPlaceRules.Moved,
                    NpcPlaceRules.MovedEvery,
                    NpcPlaceRules.AllEvery,
                    NpcPlaceRules.Snap,
                    NpcPlaceRules.Close,
                    NpcPlaceRules.SlideSpeed,
                    NpcPlaceRules.SlidePerDistance,
                };

                limits.ShouldBe(new[] { 30f, 0.5f, 4f, 600f, 15f, 250f, 4f });
            }

            [Fact]
            public void Pins_the_message_size()
            {
                NpcPlaceRules.PerMessage.ShouldBe(200);
            }
        }

        public class Shared
        {
            [Theory]
            [InlineData(0, false)]
            [InlineData(1, true)]
            [InlineData(999, true)]
            [InlineData(1000, false)]
            [InlineData(-5, false)]
            public void Shares_ids_from_one_to_below_the_limit(
                int id,
                bool shared)
            {
                var result = NpcPlaceRules.Shared(id, 1000);

                result.ShouldBe(shared);
            }
        }

        public class Movable
        {
            [Fact]
            public void Moves_a_live_npc()
            {
                var movable = NpcPlaceRules.Movable(Doctor);

                movable.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_one_with_no_character()
            {
                var npc = Doctor with { HasCharacter = false };

                var movable = NpcPlaceRules.Movable(npc);

                movable.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_dead_one()
            {
                var npc = Doctor with { Alive = false };

                var movable = NpcPlaceRules.Movable(npc);

                movable.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_one_run_by_the_enemies()
            {
                var npc = Doctor with { RunByEnemies = true };

                var movable = NpcPlaceRules.Movable(npc);

                movable.ShouldBeFalse();
            }
        }

        public class Tick
        {
            [Fact]
            public void Ticks_exactly_when_due()
            {
                var tick = NpcPlaceRules.Tick(1, new Seconds(10f), Timers);

                tick.Sends.ShouldBeTrue();
            }

            [Fact]
            public void Waits_the_next_tick_half_a_second()
            {
                var tick = NpcPlaceRules.Tick(1, new Seconds(10f), Timers);

                tick.Timers.NextMoved.Value.ShouldBe(10.5f);
            }

            [Fact]
            public void Sends_only_those_that_moved_before_all_are_due()
            {
                var tick = NpcPlaceRules.Tick(1, new Seconds(10f), Timers);

                tick.All.ShouldBeFalse();
            }

            [Fact]
            public void Keeps_the_all_timer_before_it_is_due()
            {
                var tick = NpcPlaceRules.Tick(1, new Seconds(10f), Timers);

                tick.Timers.NextAll.Value.ShouldBe(12f);
            }

            [Fact]
            public void Waits_just_before_it_is_due()
            {
                var tick = NpcPlaceRules.Tick(1, new Seconds(9.9f), Timers);

                tick.ShouldBe(new NpcTick { Timers = Timers });
            }

            [Fact]
            public void Waits_with_nobody_joined()
            {
                var tick = NpcPlaceRules.Tick(0, new Seconds(20f), Timers);

                tick.ShouldBe(new NpcTick { Timers = Timers });
            }

            [Fact]
            public void Sends_all_exactly_when_due()
            {
                var tick = NpcPlaceRules.Tick(1, new Seconds(12f), Timers);

                tick.All.ShouldBeTrue();
            }

            [Fact]
            public void Waits_four_seconds_for_the_next_all()
            {
                var tick = NpcPlaceRules.Tick(1, new Seconds(12f), Timers);

                tick.Timers.NextAll.Value.ShouldBe(16f);
            }

            [Fact]
            public void Sends_all_at_the_first_tick()
            {
                var tick = NpcPlaceRules.Tick(2, new Seconds(0f), default);

                tick.All.ShouldBeTrue();
            }
        }

        public class Sends
        {
            [Fact]
            public void Skips_one_moved_exactly_the_limit()
            {
                var sends = NpcPlaceRules.Sends(AtMoved);

                sends.ShouldBeFalse();
            }

            [Fact]
            public void Sends_one_moved_just_past_the_limit()
            {
                var npc = AtMoved with { At = new Vec2(30.01f, 0f) };

                var sends = NpcPlaceRules.Sends(npc);

                sends.ShouldBeTrue();
            }

            [Fact]
            public void Measures_the_move_on_both_axes()
            {
                var npc = AtMoved with { At = new Vec2(22f, 22f) };

                var sends = NpcPlaceRules.Sends(npc);

                sends.ShouldBeTrue();
            }

            [Fact]
            public void Sends_one_never_sent()
            {
                var npc = AtMoved with { Sent = false, At = default };

                var sends = NpcPlaceRules.Sends(npc);

                sends.ShouldBeTrue();
            }

            [Fact]
            public void Sends_one_that_stood_still_when_sending_all()
            {
                var npc = AtMoved with { All = true, At = default };

                var sends = NpcPlaceRules.Sends(npc);

                sends.ShouldBeTrue();
            }
        }

        public class BatchSize
        {
            [Theory]
            [InlineData(5, 0, 5)]
            [InlineData(200, 0, 200)]
            [InlineData(201, 0, 200)]
            [InlineData(201, 200, 1)]
            [InlineData(450, 400, 50)]
            public void Puts_at_most_two_hundred_in_a_message(
                int count,
                int start,
                int size)
            {
                var result = NpcPlaceRules.BatchSize(count, start);

                result.ShouldBe(size);
            }
        }

        public class TakesPos
        {
            [Theory]
            [InlineData(true, true, true)]
            [InlineData(false, true, false)]
            [InlineData(true, false, false)]
            public void Takes_it_from_the_host_in_a_world(
                bool fromHost,
                bool world,
                bool takes)
            {
                var result = NpcPlaceRules.TakesPos(fromHost, world);

                result.ShouldBe(takes);
            }
        }

        public class Follow
        {
            [Fact]
            public void Skips_an_id_with_no_object()
            {
                var npc = Off with { HasObject = false, Movable = false };

                var step = NpcPlaceRules.Follow(npc);

                step.Move.ShouldBe(NpcMove.Skip);
            }

            [Fact]
            public void Forgets_one_that_cannot_move()
            {
                var npc = Off with { Movable = false };

                var step = NpcPlaceRules.Follow(npc);

                step.Move.ShouldBe(NpcMove.Forget);
            }

            [Fact]
            public void Leaves_a_copy_exactly_close()
            {
                var npc = Off with { Target = new Vec2(15f, 0f) };

                var step = NpcPlaceRules.Follow(npc);

                step.Move.ShouldBe(NpcMove.Leave);
            }

            [Fact]
            public void Moves_a_copy_just_past_close()
            {
                var npc = Off with { Target = new Vec2(15.01f, 0f) };

                var step = NpcPlaceRules.Follow(npc);

                step.Move.ShouldBe(NpcMove.Step);
            }

            [Fact]
            public void Slides_a_copy_exactly_at_the_snap()
            {
                var npc = Off with { Target = new Vec2(600f, 0f) };

                var step = NpcPlaceRules.Follow(npc);

                step.Move.ShouldBe(NpcMove.Step);
            }

            [Fact]
            public void Snaps_a_copy_just_past_the_snap()
            {
                var npc = Off with { Target = new Vec2(600.1f, 0f) };

                var step = NpcPlaceRules.Follow(npc);

                step.Move.ShouldBe(NpcMove.Snap);
            }

            [Fact]
            public void Snaps_it_to_the_host_spot_at_its_own_height()
            {
                var npc = Off with { Target = new Vec2(700f, 300f) };

                var step = NpcPlaceRules.Follow(npc);

                step.To.ShouldBe(new Vec3(700f, 5f, 300f));
            }

            [Fact]
            public void Measures_the_distance_on_the_ground()
            {
                var npc = Off with { At = new Vec3(0f, 900f, 0f) };

                var step = NpcPlaceRules.Follow(npc);

                step.Distance.ShouldBe(100f);
            }

            [Fact]
            public void Slides_at_the_least_speed_when_near()
            {
                var npc = Off with { Target = new Vec2(50f, 0f) };

                var step = NpcPlaceRules.Follow(npc);

                step.To.X.ShouldBe(25f, 0.001f);
            }

            [Fact]
            public void Slides_four_times_its_distance_when_further()
            {
                var npc = Off with { Target = new Vec2(0f, 500f) };

                var step = NpcPlaceRules.Follow(npc);

                step.To.Z.ShouldBe(200f, 0.001f);
            }

            [Fact]
            public void Puts_it_there_when_the_step_reaches()
            {
                var npc = Off with { Delta = new Seconds(1f) };

                var step = NpcPlaceRules.Follow(npc);

                step.To.ShouldBe(new Vec3(100f, 5f, 0f));
            }

            [Fact]
            public void Leaves_it_where_it_is_while_paused()
            {
                var npc = Off with { Delta = default };

                var step = NpcPlaceRules.Follow(npc);

                step.To.ShouldBe(new Vec3(0f, 5f, 0f));
            }
        }
    }
}
