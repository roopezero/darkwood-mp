using DarkwoodMP.World.Rolls;

namespace DarkwoodMP.Tests.World.Rolls
{
    public class WeatherKeyTests
    {
        /// Rain today at 300 for 120, no lightning, fog in on day 3 at 400.
        static readonly WeatherFacts Rainy = new WeatherFacts
        {
            RainToday = true,
            Start = 300f,
            Duration = 120f,
            FogIn = new FogTime { Set = true, Day = 3, Time = 400 },
        };

        public class Of
        {
            [Fact]
            public void Writes_the_days_plan()
            {
                WeatherKey.Of(Rainy).ShouldBe(
                    "rain 1 at 300 for 120, lightning 0 0 0, fog 3/400-none");
            }

            [Fact]
            public void Writes_the_lightning()
            {
                var rain = Rainy with
                {
                    Lightning = true,
                    LightningStart = 250f,
                    LightningFor = 40f,
                };

                WeatherKey.Of(rain).ShouldBe(
                    "rain 1 at 300 for 120, lightning 1 250 40,"
                    + " fog 3/400-none");
            }

            [Fact]
            public void Rounds_the_times_to_whole_numbers()
            {
                var rain = Rainy with { Start = 299.6f, Duration = 120.4f };

                WeatherKey.Of(rain).ShouldBe(
                    "rain 1 at 300 for 120, lightning 0 0 0, fog 3/400-none");
            }

            [Fact]
            public void Writes_no_fog_as_none()
            {
                var rain = new WeatherFacts();

                WeatherKey.Of(rain).ShouldBe(
                    "rain 0 at 0 for 0, lightning 0 0 0, fog none-none");
            }

            [Fact]
            public void Writes_the_fog_out()
            {
                var rain = Rainy with
                {
                    FogOut = new FogTime { Set = true, Day = 4, Time = 30 },
                };

                WeatherKey.Of(rain).ShouldBe(
                    "rain 1 at 300 for 120, lightning 0 0 0, fog 3/400-4/30");
            }
        }
    }
}
