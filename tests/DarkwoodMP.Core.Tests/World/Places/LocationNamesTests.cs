using DarkwoodMP.World.Places;

namespace DarkwoodMP.Tests.World.Places
{
    public class LocationNamesTests
    {
        static readonly string[] Build = new[]
        {
            "Assets/Scenes/Menu.unity",
            "Assets/Locations/Bunker.unity",
            "Assets/Locations/Dreams/Dream1.unity",
            null,
        };

        public class IsReal
        {
            [Theory]
            [InlineData("Bunker", true)]
            [InlineData("bunker", true)]
            [InlineData("Dream1", true)]
            [InlineData("Menu", false)]
            [InlineData("Church", false)]
            public void Knows_the_builds_locations(string name, bool real)
            {
                new LocationNames(Build).IsReal(name).ShouldBe(real);
            }

            [Fact]
            public void Takes_any_name_when_none_are_found()
            {
                var names = new LocationNames(new[] { "Assets/Menu.unity" });

                names.IsReal("Anything").ShouldBeTrue();
            }

            [Fact]
            public void Counts_the_locations_found()
            {
                new LocationNames(Build).Count.ShouldBe(2);
            }
        }
    }
}
