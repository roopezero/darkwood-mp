using DarkwoodMP.Sync.Players;

namespace DarkwoodMP.Tests.Sync.Players
{
    public class SpawnRulesTests
    {
        public class Limits
        {
            [Fact]
            public void Pins_the_search_limits()
            {
                var limits = new[]
                {
                    (float)SpawnRules.Angles,
                    SpawnRules.RingCount,
                    SpawnRules.NoCapsuleRadius,
                    SpawnRules.MinRadius,
                    SpawnRules.Gap,
                    SpawnRules.BackgroundShare,
                    SpawnRules.SphereShare,
                    SpawnRules.MaxHits,
                    SpawnRules.GroundLayer,
                    SpawnRules.RayFrom,
                };

                var pinned = new[]
                {
                    12f, 5f, 15f, 10f, 10f, 0.5f, 1.1f, 64f, 1f, 1f,
                };

                limits.ShouldBe(pinned);
            }

            [Fact]
            public void Pins_the_rings()
            {
                var rings = new[]
                {
                    SpawnRules.RingAt(0),
                    SpawnRules.RingAt(1),
                    SpawnRules.RingAt(2),
                    SpawnRules.RingAt(3),
                    SpawnRules.RingAt(4),
                };

                rings.ShouldBe(new[] { 2.5f, 4f, 6f, 8f, 11f });
            }

            [Fact]
            public void Pins_unitys_degrees_to_radians()
            {
                var factor = SpawnRules.Deg2Rad;

                factor.ShouldBe(0.0174532924f);
            }
        }

        public class BodyRadius
        {
            [Fact]
            public void Scales_the_capsule_by_the_larger_ground_scale()
            {
                var radius = SpawnRules.BodyRadius(
                    true, 10f, new Vec2(2f, -3f));

                radius.ShouldBe(30f);
            }

            [Fact]
            public void Holds_it_to_at_least_10()
            {
                var radius = SpawnRules.BodyRadius(
                    true, 5f, new Vec2(1f, 1f));

                radius.ShouldBe(10f);
            }

            [Fact]
            public void Takes_15_without_a_capsule()
            {
                var radius = SpawnRules.BodyRadius(
                    false, 40f, new Vec2(1f, 1f));

                radius.ShouldBe(15f);
            }
        }

        public class Apart
        {
            [Fact]
            public void Keeps_two_radii_and_10_apart()
            {
                var apart = SpawnRules.Apart(15f);

                apart.ShouldBe(40f);
            }
        }

        public class Searched
        {
            [Fact]
            public void Searches_eleven_radii_out()
            {
                var searched = SpawnRules.Searched(15f);

                searched.ShouldBe(165f);
            }
        }

        public class KeepsClear
        {
            [Theory]
            [InlineData(true, false, true, true)]
            [InlineData(false, false, true, false)]
            [InlineData(true, true, true, false)]
            [InlineData(true, false, false, false)]
            public void Keeps_clear_of_a_player_in_our_place(
                bool hasLast,
                bool spectating,
                bool same,
                bool clear)
            {
                var result = SpawnRules.KeepsClear(hasLast, spectating, same);

                result.ShouldBe(clear);
            }
        }

        public class Around
        {
            static readonly Vec3 Want = new Vec3(100f, 7f, 200f);

            [Fact]
            public void Tries_twelve_angles_on_five_rings()
            {
                var spots = SpawnRules.Around(Want, 10f, 0f);

                spots.Count.ShouldBe(60);
            }

            [Fact]
            public void Starts_at_the_start_angle_on_the_smallest_ring()
            {
                var spots = SpawnRules.Around(Want, 10f, 0f);

                new[] { spots[0].X, spots[0].Z }
                    .ShouldBe(new[] { 125f, 200f }, 1e-3);
            }

            [Fact]
            public void Turns_30_degrees_each_try()
            {
                var spots = SpawnRules.Around(Want, 10f, 0f);

                new[] { spots[3].X, spots[3].Z }
                    .ShouldBe(new[] { 100f, 225f }, 1e-3);
            }

            [Fact]
            public void Turns_from_the_rolled_start()
            {
                var spots = SpawnRules.Around(Want, 10f, 90f);

                new[] { spots[0].X, spots[0].Z }
                    .ShouldBe(new[] { 100f, 225f }, 1e-3);
            }

            [Fact]
            public void Goes_to_the_next_ring_after_twelve()
            {
                var spots = SpawnRules.Around(Want, 10f, 0f);

                spots[12].X.ShouldBe(140f, 1e-3f);
            }

            [Fact]
            public void Ends_on_the_largest_ring()
            {
                var spots = SpawnRules.Around(Want, 10f, 0f);

                spots[48].X.ShouldBe(210f, 1e-3f);
            }

            [Fact]
            public void Keeps_the_wanted_height()
            {
                var spots = SpawnRules.Around(Want, 10f, 45f);

                spots[20].Y.ShouldBe(7f);
            }
        }

        public class ClearOfOthers
        {
            static readonly Vec2 At = new Vec2(0f, 0f);

            [Fact]
            public void Takes_a_spot_exactly_apart()
            {
                var others = new[] { new Vec2(40f, 0f) };

                var clear = SpawnRules.ClearOfOthers(At, others, 40f);

                clear.ShouldBeTrue();
            }

            [Fact]
            public void Refuses_a_spot_just_closer()
            {
                var others = new[] { new Vec2(0f, 39.9f) };

                var clear = SpawnRules.ClearOfOthers(At, others, 40f);

                clear.ShouldBeFalse();
            }

            [Fact]
            public void Refuses_a_spot_near_any_of_them()
            {
                var others = new[] { new Vec2(500f, 0f), new Vec2(1f, 1f) };

                var clear = SpawnRules.ClearOfOthers(At, others, 40f);

                clear.ShouldBeFalse();
            }

            [Fact]
            public void Takes_any_spot_with_nobody_else()
            {
                var clear = SpawnRules.ClearOfOthers(At, new Vec2[0], 40f);

                clear.ShouldBeTrue();
            }
        }

        public class IsSolid
        {
            static readonly SolidFacts Wall = new SolidFacts
            {
                Exists = true,
                Layer = 0,
            };

            [Fact]
            public void Counts_a_wall()
            {
                var solid = SpawnRules.IsSolid(Wall);

                solid.ShouldBeTrue();
            }

            [Fact]
            public void Skips_a_destroyed_collider()
            {
                var c = Wall with { Exists = false };

                var solid = SpawnRules.IsSolid(c);

                solid.ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_trigger()
            {
                var c = Wall with { Trigger = true };

                var solid = SpawnRules.IsSolid(c);

                solid.ShouldBeFalse();
            }

            [Fact]
            public void Skips_the_background()
            {
                var c = Wall with { Background = true };

                var solid = SpawnRules.IsSolid(c);

                solid.ShouldBeFalse();
            }

            [Fact]
            public void Skips_the_ground()
            {
                var c = Wall with { Layer = 1 };

                var solid = SpawnRules.IsSolid(c);

                solid.ShouldBeFalse();
            }

            [Fact]
            public void Skips_the_player_being_placed()
            {
                var c = Wall with { OfPlayer = true };

                var solid = SpawnRules.IsSolid(c);

                solid.ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_stand_in()
            {
                var c = Wall with { OfProxy = true };

                var solid = SpawnRules.IsSolid(c);

                solid.ShouldBeFalse();
            }
        }

        public class WalkableEnough
        {
            [Theory]
            [InlineData(false, false, true)]
            [InlineData(true, true, true)]
            [InlineData(true, false, false)]
            public void Asks_the_grid_only_where_the_wanted_spot_is_on_it(
                bool gridHere,
                bool walkable,
                bool enough)
            {
                var result = SpawnRules.WalkableEnough(gridHere, walkable);

                result.ShouldBe(enough);
            }
        }

        public class ChecksRay
        {
            [Theory]
            [InlineData(1f, false)]
            [InlineData(1.01f, true)]
            public void Casts_beyond_one_unit(float length, bool casts)
            {
                var result = SpawnRules.ChecksRay(length);

                result.ShouldBe(casts);
            }
        }

        public class BlocksRay
        {
            [Theory]
            [InlineData(false, true, true)]
            [InlineData(true, true, false)]
            [InlineData(false, false, false)]
            public void Blocks_with_a_solid_without_a_rigidbody(
                bool rigidbody,
                bool solid,
                bool blocks)
            {
                var result = SpawnRules.BlocksRay(rigidbody, solid);

                result.ShouldBe(blocks);
            }
        }
    }
}
