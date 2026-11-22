using DarkwoodMP.Sync.Players;

namespace DarkwoodMP.Tests.Sync.Players
{
    public class LightGridRulesTests
    {
        /// A 100 by 50 grid of 10-unit nodes from the origin, node light
        /// radius 2, with the light in the middle.
        static readonly LightGridFacts Inside = new LightGridFacts
        {
            Pathfinding = true,
            Graph = true,
            Nodes = 5000,
            NodeSize = 10f,
            Width = 100,
            Depth = 50,
            Light = new Vec3(500f, 0f, 250f),
            Radius = 2,
        };

        static LightGridFacts At(float x, float z) =>
            Inside with { Light = new Vec3(x, 0f, z) };

        public class SkipsLookup
        {
            [Fact]
            public void Lets_the_game_light_a_light_in_the_grid()
            {
                var skips = LightGridRules.SkipsLookup(Inside);

                skips.ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_light_far_off_the_grid()
            {
                var skips = LightGridRules.SkipsLookup(At(80000f, 250f));

                skips.ShouldBeTrue();
            }

            [Theory]
            [InlineData(-20f, false)]
            [InlineData(-30f, true)]
            [InlineData(1010f, false)]
            [InlineData(1020f, true)]
            public void Takes_the_radius_past_each_x_edge(float x, bool skips)
            {
                var result = LightGridRules.SkipsLookup(At(x, 250f));

                result.ShouldBe(skips);
            }

            [Theory]
            [InlineData(-20f, false)]
            [InlineData(-30f, true)]
            [InlineData(510f, false)]
            [InlineData(520f, true)]
            public void Takes_the_radius_past_each_z_edge(float z, bool skips)
            {
                var result = LightGridRules.SkipsLookup(At(500f, z));

                result.ShouldBe(skips);
            }

            [Theory]
            [InlineData(-25f, false)]
            [InlineData(-35f, true)]
            [InlineData(1015f, true)]
            [InlineData(1005f, false)]
            public void Rounds_halves_to_even(float x, bool skips)
            {
                var result = LightGridRules.SkipsLookup(At(x, 250f));

                result.ShouldBe(skips);
            }

            [Fact]
            public void Measures_from_the_first_node()
            {
                var light = At(-30f, 250f) with
                {
                    Origin = new Vec3(-100f, 0f, 0f),
                };

                var skips = LightGridRules.SkipsLookup(light);

                skips.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_it_to_the_game_without_pathfinding()
            {
                var light = At(80000f, 0f) with { Pathfinding = false };

                var skips = LightGridRules.SkipsLookup(light);

                skips.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_it_to_the_game_without_a_grid()
            {
                var light = At(80000f, 0f) with { Graph = false };

                var skips = LightGridRules.SkipsLookup(light);

                skips.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_it_to_the_game_without_nodes()
            {
                var light = At(80000f, 0f) with { Nodes = 0 };

                var skips = LightGridRules.SkipsLookup(light);

                skips.ShouldBeFalse();
            }

            [Theory]
            [InlineData(0f)]
            [InlineData(-1f)]
            public void Leaves_it_to_the_game_without_a_node_size(float size)
            {
                var light = At(80000f, 0f) with { NodeSize = size };

                var skips = LightGridRules.SkipsLookup(light);

                skips.ShouldBeFalse();
            }
        }
    }
}
