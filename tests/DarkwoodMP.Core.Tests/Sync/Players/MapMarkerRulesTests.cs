using DarkwoodMP.Sync.Players;

namespace DarkwoodMP.Tests.Sync.Players
{
    public class MapMarkerRulesTests
    {
        /// A playing puppet in the world.
        static readonly MarkerFacts Playing = new MarkerFacts
        {
            HasRoot = true,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_map_type_and_raise()
            {
                var pinned = (MapMarkerRules.WorldType, MapMarkerRules.Raise);

                pinned.ShouldBe(("World", 15f));
            }
        }

        public class OnWorldMap
        {
            [Theory]
            [InlineData(true, "World", true)]
            [InlineData(false, "World", false)]
            [InlineData(true, "Dream", false)]
            [InlineData(true, null, false)]
            public void Marks_only_the_open_world_map(
                bool opened,
                string type,
                bool world)
            {
                var result = MapMarkerRules.OnWorldMap(opened, type);

                result.ShouldBe(world);
            }
        }

        public class OnMap
        {
            [Fact]
            public void Shows_a_playing_puppet()
            {
                var on = MapMarkerRules.OnMap(Playing);

                on.ShouldBeTrue();
            }

            [Fact]
            public void Hides_a_puppet_with_no_root()
            {
                var puppet = Playing with { HasRoot = false };

                var on = MapMarkerRules.OnMap(puppet);

                on.ShouldBeFalse();
            }

            [Fact]
            public void Hides_a_puppet_that_is_away()
            {
                var puppet = Playing with { Away = true };

                var on = MapMarkerRules.OnMap(puppet);

                on.ShouldBeFalse();
            }

            [Fact]
            public void Hides_a_spectator()
            {
                var puppet = Playing with { Spectating = true };

                var on = MapMarkerRules.OnMap(puppet);

                on.ShouldBeFalse();
            }

            [Fact]
            public void Shows_the_dummy_once_it_has_a_marker()
            {
                var puppet = Playing with { Dummy = true };

                var on = MapMarkerRules.OnMap(puppet);

                on.ShouldBeTrue();
            }
        }

        public class Marks
        {
            [Fact]
            public void Marks_a_playing_puppet()
            {
                var marks = MapMarkerRules.Marks(Playing);

                marks.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_the_dummy_off_the_map()
            {
                var puppet = Playing with { Dummy = true };

                var marks = MapMarkerRules.Marks(puppet);

                marks.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_puppet_that_is_away()
            {
                var puppet = Playing with { Away = true };

                var marks = MapMarkerRules.Marks(puppet);

                marks.ShouldBeFalse();
            }
        }

        public class LocalSpot
        {
            [Fact]
            public void Scales_down_and_raises_the_world_position()
            {
                var spot = MapMarkerRules.LocalSpot(
                    new Vec3(100f, 10f, -40f), 4f);

                new[] { spot.X, spot.Y, spot.Z }
                    .ShouldBe(new[] { 25f, 17.5f, -10f });
            }
        }

        public class IconShift
        {
            [Fact]
            public void Scales_the_offset_to_the_resolution()
            {
                var shift = MapMarkerRules.IconShift(
                    new Vec2(10f, -4f), new Vec2(2f, 0.5f));

                new[] { shift.X, shift.Y, shift.Z }
                    .ShouldBe(new[] { 20f, 0f, -2f });
            }
        }
    }
}
