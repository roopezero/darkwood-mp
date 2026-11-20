using DarkwoodMP.World.Doors;

namespace DarkwoodMP.Tests.World.Doors
{
    public class LeafRulesTests
    {
        /// An open door whose leaf is on, with a free Rigidbody.
        static readonly LeafDoorFacts Open = new LeafDoorFacts
        {
            Exists = true,
            HasBody = true,
            HasRigidbody = true,
            Opened = true,
            Active = true,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_angles()
            {
                var angles = new[]
                {
                    LeafRules.ShutZ,
                    LeafRules.TurnedAngle,
                    LeafRules.FullTurn,
                    LeafRules.SaveLimit,
                    LeafRules.FacingSlack,
                };

                angles.ShouldBe(new[] { 90f, 10f, 180f, 105f, 1f });
            }
        }

        public class Signed
        {
            [Theory]
            [InlineData(90f, 0f)]
            [InlineData(80f, 10f)]
            [InlineData(100f, -10f)]
            [InlineData(0f, 90f)]
            [InlineData(270f, 180f)]
            [InlineData(300f, 150f)]
            public void Measures_the_turn_from_shut(float z, float signed)
            {
                var angle = LeafRules.Signed(z);

                angle.ShouldBe(signed);
            }
        }

        public class Turned
        {
            [Theory]
            [InlineData(10f, false)]
            [InlineData(-10f, false)]
            [InlineData(10.5f, true)]
            [InlineData(-10.5f, true)]
            public void Counts_a_leaf_past_ten_degrees_as_open(
                float signed,
                bool turned)
            {
                var open = LeafRules.Turned(signed);

                open.ShouldBe(turned);
            }
        }

        public class MaxTurn
        {
            static readonly HingeFacts Limited = new HingeFacts
            {
                Present = true,
                UseLimits = true,
                Min = -110f,
                Max = 110f,
            };

            [Fact]
            public void Takes_the_hinges_limit()
            {
                var max = LeafRules.MaxTurn(Limited);

                max.ShouldBe(110f);
            }

            [Fact]
            public void Takes_the_larger_limit_either_way()
            {
                var hinge = Limited with { Min = -30f, Max = 50f };

                var max = LeafRules.MaxTurn(hinge);

                max.ShouldBe(50f);
            }

            [Fact]
            public void Holds_a_limit_within_a_half_turn()
            {
                var hinge = Limited with { Min = -200f };

                var max = LeafRules.MaxTurn(hinge);

                max.ShouldBe(180f);
            }

            [Fact]
            public void Turns_all_the_way_without_limits()
            {
                var hinge = Limited with { UseLimits = false };

                var max = LeafRules.MaxTurn(hinge);

                max.ShouldBe(180f);
            }

            [Fact]
            public void Turns_all_the_way_without_a_hinge()
            {
                var max = LeafRules.MaxTurn(new HingeFacts());

                max.ShouldBe(180f);
            }
        }

        public class SaveAngle
        {
            [Theory]
            [InlineData(40f, 40f)]
            [InlineData(105f, 105f)]
            [InlineData(106f, 105f)]
            [InlineData(-120f, -105f)]
            public void Keeps_the_saves_range(float signed, float saved)
            {
                var rot = LeafRules.SaveAngle(signed);

                rot.ShouldBe(saved);
            }
        }

        public class TurnBy
        {
            [Fact]
            public void Turns_a_shut_leaf_to_the_angle()
            {
                var by = LeafRules.TurnBy(90f, 30f, 110f);

                by.ShouldBe(-30f);
            }

            [Fact]
            public void Holds_the_angle_within_the_max_turn()
            {
                var by = LeafRules.TurnBy(90f, 150f, 110f);

                by.ShouldBe(-110f);
            }
        }

        public class Side
        {
            static readonly Vec2 Body = new Vec2(0f, 0f);

            [Theory]
            [InlineData(180f, 0f, 1f, -1f)]
            [InlineData(180f, 0f, -1f, 1f)]
            [InlineData(0f, 0f, 1f, 1f)]
            [InlineData(0f, 0f, -1f, -1f)]
            [InlineData(90f, 1f, 0f, 1f)]
            [InlineData(90f, -1f, 0f, -1f)]
            [InlineData(270f, 1f, 0f, -1f)]
            [InlineData(270f, -1f, 0f, 1f)]
            [InlineData(45f, 1f, 0f, -1f)]
            [InlineData(45f, -1f, 0f, 1f)]
            public void Pushes_away_from_the_opener(
                float yaw,
                float x,
                float z,
                float side)
            {
                var push = LeafRules.Side(yaw, new Vec2(x, z), Body);

                push.ShouldBe(side);
            }

            [Theory]
            [InlineData(179.5f, 1f, -1f, 1f)]
            [InlineData(179f, 1f, -1f, -1f)]
            [InlineData(0.5f, 1f, 1f, 1f)]
            [InlineData(1f, 1f, 1f, -1f)]
            public void Follows_a_facing_only_within_a_degree(
                float yaw,
                float x,
                float z,
                float side)
            {
                var push = LeafRules.Side(yaw, new Vec2(x, z), Body);

                push.ShouldBe(side);
            }

            [Fact]
            public void Misses_a_yaw_just_under_a_full_turn()
            {
                var opener = new Vec2(1f, 1f);

                var push = LeafRules.Side(359.5f, opener, Body);

                push.ShouldBe(-1f);
            }
        }

        public class RemembersOpen
        {
            [Fact]
            public void Remembers_a_door_opened_while_its_leaf_is_off()
            {
                var door = Open with { Active = false };

                var remembers = LeafRules.RemembersOpen(door);

                remembers.ShouldBeTrue();
            }

            [Fact]
            public void Forgets_a_door_whose_leaf_is_on()
            {
                var remembers = LeafRules.RemembersOpen(Open);

                remembers.ShouldBeFalse();
            }

            [Fact]
            public void Forgets_a_shut_door()
            {
                var door = Open with { Active = false, Opened = false };

                var remembers = LeafRules.RemembersOpen(door);

                remembers.ShouldBeFalse();
            }

            [Fact]
            public void Forgets_a_door_with_no_leaf()
            {
                var door = Open with { Active = false, HasBody = false };

                var remembers = LeafRules.RemembersOpen(door);

                remembers.ShouldBeFalse();
            }
        }

        public class SwingsOnEnable
        {
            [Fact]
            public void Swings_an_open_door_still_shut_here()
            {
                var swings = LeafRules.SwingsOnEnable(Open, 10f);

                swings.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_a_leaf_turned_already()
            {
                var swings = LeafRules.SwingsOnEnable(Open, 10.5f);

                swings.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_door_shut_since()
            {
                var door = Open with { Opened = false };

                var swings = LeafRules.SwingsOnEnable(door, 0f);

                swings.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_broken_door()
            {
                var door = Open with { Destroyed = true };

                var swings = LeafRules.SwingsOnEnable(door, 0f);

                swings.ShouldBeFalse();
            }
        }

        public class PushesOpen
        {
            [Fact]
            public void Pushes_an_open_door_with_a_free_body()
            {
                var pushes = LeafRules.PushesOpen(Open);

                pushes.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_a_kinematic_body()
            {
                var door = Open with { Kinematic = true };

                var pushes = LeafRules.PushesOpen(door);

                pushes.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_door_with_no_rigidbody()
            {
                var door = Open with { HasRigidbody = false };

                var pushes = LeafRules.PushesOpen(door);

                pushes.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_door_gone_meanwhile()
            {
                var door = Open with { Exists = false };

                var pushes = LeafRules.PushesOpen(door);

                pushes.ShouldBeFalse();
            }
        }

        public class TouchDistance
        {
            /// A leaf from a hinge at the origin to its edge at x = 20.
            static readonly LeafShape Leaf = new LeafShape
            {
                HasHinge = true,
                Hinge = new Vec2(0f, 0f),
                Centre = new Vec2(10f, 0f),
            };

            [Theory]
            [InlineData(5f, 3f, 3f)]
            [InlineData(20f, 4f, 4f)]
            [InlineData(30f, 0f, 10f)]
            [InlineData(-3f, 4f, 5f)]
            public void Measures_to_the_leaf_from_hinge_to_edge(
                float x,
                float z,
                float distance)
            {
                var d = LeafRules.TouchDistance(Leaf, new Vec2(x, z));

                d.ShouldBe(distance, 1e-4f);
            }

            [Fact]
            public void Measures_to_the_centre_without_a_hinge()
            {
                var leaf = Leaf with { HasHinge = false };

                var d = LeafRules.TouchDistance(leaf, new Vec2(13f, 4f));

                d.ShouldBe(5f, 1e-4f);
            }

            [Fact]
            public void Measures_to_the_hinge_of_a_leaf_with_no_length()
            {
                var leaf = Leaf with { Centre = new Vec2(0f, 0f) };

                var d = LeafRules.TouchDistance(leaf, new Vec2(3f, 4f));

                d.ShouldBe(5f, 1e-4f);
            }
        }

        public class DeltaAngle
        {
            [Theory]
            [InlineData(0f, 90f, 90f)]
            [InlineData(0f, 270f, -90f)]
            [InlineData(350f, 10f, 20f)]
            [InlineData(10f, 350f, -20f)]
            [InlineData(0f, 180f, 180f)]
            [InlineData(0f, -180f, 180f)]
            public void Takes_the_shortest_turn(
                float current,
                float target,
                float delta)
            {
                var d = LeafRules.DeltaAngle(current, target);

                d.ShouldBe(delta);
            }
        }
    }
}
