using DarkwoodMP.Sync.Players;

namespace DarkwoodMP.Tests.Sync.Players
{
    public class SeparationRulesTests
    {
        public class Limits
        {
            [Fact]
            public void Pins_the_separation_limits()
            {
                var limits = new[]
                {
                    SeparationRules.MaxSpeed,
                    SeparationRules.DefaultRadius,
                    SeparationRules.MinRadius,
                    SeparationRules.MaxRadius,
                    SeparationRules.Overlap,
                    SeparationRules.MaxDelta,
                };

                var pinned = new[] { 120f, 18f, 1f, 60f, 0.01f, 0.05f };

                limits.ShouldBe(pinned);
            }
        }

        public class Eases
        {
            /// Our living player with a puppet near, in a world.
            static readonly SeparationFacts Playing = new SeparationFacts
            {
                InWorld = true,
                Puppets = 1,
                HasPlayer = true,
                Alive = true,
                HasBody = true,
            };

            [Fact]
            public void Eases_a_player_in_play()
            {
                var eases = SeparationRules.Eases(Playing);

                eases.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_it_with_the_setting_off()
            {
                var frame = Playing with { SwitchedOff = true };

                var eases = SeparationRules.Eases(frame);

                eases.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_it_out_of_a_world()
            {
                var frame = Playing with { InWorld = false };

                var eases = SeparationRules.Eases(frame);

                eases.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_it_with_no_puppets()
            {
                var frame = Playing with { Puppets = 0 };

                var eases = SeparationRules.Eases(frame);

                eases.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_it_with_no_player()
            {
                var frame = Playing with { HasPlayer = false };

                var eases = SeparationRules.Eases(frame);

                eases.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_dead_player()
            {
                var frame = Playing with { Alive = false };

                var eases = SeparationRules.Eases(frame);

                eases.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_dying_player()
            {
                var frame = Playing with { Dying = true };

                var eases = SeparationRules.Eases(frame);

                eases.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_player_whose_inputs_are_off()
            {
                var frame = Playing with { ForbidInputs = true };

                var eases = SeparationRules.Eases(frame);

                eases.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_player_without_a_rigidbody()
            {
                var frame = Playing with { HasBody = false };

                var eases = SeparationRules.Eases(frame);

                eases.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_kinematic_player()
            {
                var frame = Playing with { Kinematic = true };

                var eases = SeparationRules.Eases(frame);

                eases.ShouldBeFalse();
            }
        }

        public class Radius
        {
            static readonly ColliderFacts Solid = new ColliderFacts
            {
                Exists = true,
                Enabled = true,
                Extents = new Vec2(20f, 25f),
            };

            [Fact]
            public void Takes_the_largest_ground_extent()
            {
                var wide = Solid with { Extents = new Vec2(30f, 5f) };

                var radius = SeparationRules.Radius(new[] { Solid, wide });

                radius.ShouldBe(30f);
            }

            [Fact]
            public void Skips_a_trigger()
            {
                var big = Solid with
                {
                    Trigger = true,
                    Extents = new Vec2(50f, 50f),
                };

                var radius = SeparationRules.Radius(new[] { Solid, big });

                radius.ShouldBe(25f);
            }

            [Fact]
            public void Skips_a_disabled_collider()
            {
                var big = Solid with
                {
                    Enabled = false,
                    Extents = new Vec2(50f, 50f),
                };

                var radius = SeparationRules.Radius(new[] { Solid, big });

                radius.ShouldBe(25f);
            }

            [Fact]
            public void Skips_a_destroyed_collider()
            {
                var big = Solid with
                {
                    Exists = false,
                    Extents = new Vec2(50f, 50f),
                };

                var radius = SeparationRules.Radius(new[] { Solid, big });

                radius.ShouldBe(25f);
            }

            [Theory]
            [InlineData(1f, 18f)]
            [InlineData(1.01f, 1.01f)]
            [InlineData(60f, 60f)]
            [InlineData(60.01f, 18f)]
            public void Falls_back_outside_1_to_60(float extent, float radius)
            {
                var only = Solid with { Extents = new Vec2(extent, 0f) };

                var result = SeparationRules.Radius(new[] { only });

                result.ShouldBe(radius);
            }

            [Fact]
            public void Falls_back_with_no_colliders()
            {
                var radius = SeparationRules.Radius(new ColliderFacts[0]);

                radius.ShouldBe(18f);
            }
        }

        public class Distance
        {
            [Theory]
            [InlineData(20f, 40f)]
            [InlineData(0f, 36f)]
            public void Keeps_two_body_widths(float radius, float distance)
            {
                var result = SeparationRules.Distance(radius);

                result.ShouldBe(distance);
            }
        }

        public class Step
        {
            const ulong Us = 5;

            /// Ours at the origin, radius 20 (so 40 apart), a full frame.
            static PushFacts With(params PuppetBody[] puppets) =>
                new PushFacts
                {
                    LocalId = Us,
                    Radius = 20f,
                    Delta = new Seconds(0.05f),
                    Puppets = puppets,
                };

            static PuppetBody At(float x, float z) =>
                new PuppetBody
                {
                    Owner = 9,
                    Shown = true,
                    Alive = true,
                    At = new Vec2(x, z),
                };

            [Fact]
            public void Pushes_away_from_a_puppet_halfway_in()
            {
                var step = SeparationRules.Step(With(At(20f, 0f)));

                step.X.ShouldBe(-3f, 1e-4f);
            }

            [Fact]
            public void Pushes_along_the_line_between_them()
            {
                var step = SeparationRules.Step(With(At(0f, -20f)));

                step.Z.ShouldBe(3f, 1e-4f);
            }

            [Fact]
            public void Leaves_a_puppet_exactly_at_the_distance()
            {
                var step = SeparationRules.Step(With(At(40f, 0f)));

                step.ShouldBe(default(Vec2));
            }

            [Fact]
            public void Pushes_from_a_puppet_just_inside_it()
            {
                var step = SeparationRules.Step(With(At(39.9f, 0f)));

                step.X.ShouldBeLessThan(0f);
            }

            [Fact]
            public void Moves_at_full_speed_right_on_top()
            {
                var push = With(At(0f, 0f));

                var step = SeparationRules.Step(push);

                step.X.ShouldBe(6f, 1e-4f);
            }

            [Fact]
            public void Moves_the_lower_id_plus_x_when_nearly_on_top()
            {
                var step = SeparationRules.Step(With(At(0.009f, 0f)));

                step.X.ShouldBeGreaterThan(0f);
            }

            [Fact]
            public void Moves_the_higher_id_minus_x_when_on_top()
            {
                var other = At(0f, 0f) with { Owner = 2 };

                var step = SeparationRules.Step(With(other));

                step.X.ShouldBeLessThan(0f);
            }

            [Fact]
            public void Moves_away_by_direction_just_past_on_top()
            {
                var step = SeparationRules.Step(With(At(0.02f, 0f)));

                step.X.ShouldBeLessThan(0f);
            }

            [Fact]
            public void Holds_the_frame_to_a_twentieth_of_a_second()
            {
                var push = With(At(0f, 0f)) with { Delta = new Seconds(1f) };

                var step = SeparationRules.Step(push);

                step.X.ShouldBe(6f, 1e-4f);
            }

            [Fact]
            public void Moves_less_in_a_shorter_frame()
            {
                var push = With(At(0f, 0f)) with
                {
                    Delta = new Seconds(0.025f),
                };

                var step = SeparationRules.Step(push);

                step.X.ShouldBe(3f, 1e-4f);
            }

            [Fact]
            public void Holds_the_share_of_speed_to_1()
            {
                var push = With(At(0f, 0f), At(0f, 0f));

                var step = SeparationRules.Step(push);

                step.X.ShouldBe(6f, 1e-4f);
            }

            [Fact]
            public void Cancels_out_between_two_sides()
            {
                var push = With(At(20f, 0f), At(-20f, 0f));

                var step = SeparationRules.Step(push);

                step.ShouldBe(default(Vec2));
            }

            [Fact]
            public void Skips_a_hidden_puppet()
            {
                var other = At(0f, 0f) with { Shown = false };

                var step = SeparationRules.Step(With(other));

                step.ShouldBe(default(Vec2));
            }

            [Fact]
            public void Skips_a_dead_puppet()
            {
                var other = At(0f, 0f) with { Alive = false };

                var step = SeparationRules.Step(With(other));

                step.ShouldBe(default(Vec2));
            }

            [Fact]
            public void Skips_a_spectator()
            {
                var other = At(0f, 0f) with { Spectating = true };

                var step = SeparationRules.Step(With(other));

                step.ShouldBe(default(Vec2));
            }

            [Fact]
            public void Uses_the_default_width_before_a_measurement()
            {
                var push = With(At(35f, 0f)) with { Radius = 0f };

                var step = SeparationRules.Step(push);

                step.X.ShouldBeLessThan(0f);
            }
        }
    }
}
