using DarkwoodMP.Sync.Players;

namespace DarkwoodMP.Tests.Sync.Players
{
    public class SpriteCacheRulesTests
    {
        public class Limits
        {
            [Fact]
            public void Pins_the_rescan_times()
            {
                var times = new[]
                {
                    SpriteCacheRules.RescanEvery,
                    SpriteCacheRules.NeverScanned,
                };

                times.ShouldBe(new[] { 0.5f, -999f });
            }
        }

        public class Rescans
        {
            [Fact]
            public void Waits_at_exactly_half_a_second()
            {
                var rescans = SpriteCacheRules.Rescans(
                    new Seconds(10.5f), new Seconds(10f));

                rescans.ShouldBeFalse();
            }

            [Fact]
            public void Rescans_just_past_half_a_second()
            {
                var rescans = SpriteCacheRules.Rescans(
                    new Seconds(10.51f), new Seconds(10f));

                rescans.ShouldBeTrue();
            }

            [Fact]
            public void Rescans_at_once_at_the_start()
            {
                var never = new Seconds(SpriteCacheRules.NeverScanned);

                var rescans = SpriteCacheRules.Rescans(new Seconds(0f), never);

                rescans.ShouldBeTrue();
            }
        }

        public class LoadsLibrary
        {
            [Fact]
            public void Loads_a_named_library_the_first_time()
            {
                var loads = SpriteCacheRules.LoadsLibrary(
                    false, "PlayerAnims", false);

                loads.ShouldBeTrue();
            }

            [Fact]
            public void Never_tries_a_library_twice()
            {
                var loads = SpriteCacheRules.LoadsLibrary(
                    false, "PlayerAnims", true);

                loads.ShouldBeFalse();
            }

            [Fact]
            public void Loads_nothing_when_the_collection_was_found()
            {
                var loads = SpriteCacheRules.LoadsLibrary(
                    true, "PlayerAnims", false);

                loads.ShouldBeFalse();
            }

            [Theory]
            [InlineData(null)]
            [InlineData("")]
            public void Loads_nothing_without_a_name(string library)
            {
                var loads = SpriteCacheRules.LoadsLibrary(
                    false, library, false);

                loads.ShouldBeFalse();
            }
        }
    }
}
