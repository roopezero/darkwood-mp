using DarkwoodMP.Net;
using DarkwoodMP.World.Rolls;

namespace DarkwoodMP.Tests.World.Rolls
{
    public class WeatherMessageTests
    {
        /// Rain and lightning, fog in on day 3 at 2, no fog out.
        static readonly WeatherFacts Stormy = new WeatherFacts
        {
            RainToday = true,
            Start = 1f,
            Duration = 2f,
            Lightning = true,
            LightningStart = 0.5f,
            LightningFor = 0f,
            FogIn = new FogTime { Set = true, Day = 3, Time = 2 },
        };

        public class Write
        {
            [Fact]
            public void Writes_the_flags_times_and_fog()
            {
                var w = RollBytes.Begin(MsgType.Weather);
                WeatherMessage.Write(w, Stormy);

                RollBytes.Of(w).ShouldBe(new byte[]
                {
                    65,
                    3,
                    0x00, 0x00, 0x80, 0x3F,
                    0x00, 0x00, 0x00, 0x40,
                    0x00, 0x00, 0x00, 0x3F,
                    0x00, 0x00, 0x00, 0x00,
                    2, 0, 0, 0,
                    3, 0, 0, 0,
                    0xFF, 0xFF, 0xFF, 0xFF,
                    0xFF, 0xFF, 0xFF, 0xFF,
                });
            }

            [Fact]
            public void Writes_no_rain_as_no_flags()
            {
                var w = RollBytes.Begin(MsgType.Weather);
                WeatherMessage.Write(w, new WeatherFacts());

                RollBytes.Of(w)[1].ShouldBe((byte)0);
            }
        }

        public class Read
        {
            [Fact]
            public void Reads_back_what_was_written()
            {
                var w = RollBytes.Begin(MsgType.Weather);
                WeatherMessage.Write(w, Stormy);

                var rain = WeatherMessage.Read(RollBytes.Reader(w));

                WeatherKey.Of(rain).ShouldBe(WeatherKey.Of(Stormy));
                rain.LightningStart.ShouldBe(0.5f);
                rain.FogOut.Set.ShouldBeFalse();
            }

            [Fact]
            public void Reads_a_negative_fog_time_as_none()
            {
                var w = RollBytes.Begin(MsgType.Weather);
                w.W.Write((byte)0);
                for (int i = 0; i < 4; i++)
                {
                    w.W.Write(0f);
                }

                w.W.Write(-1);
                w.W.Write(4);
                w.W.Write(5);
                w.W.Write(6);

                var rain = WeatherMessage.Read(RollBytes.Reader(w));

                rain.FogIn.Set.ShouldBeFalse();
                rain.FogOut.Day.ShouldBe(6);
                rain.FogOut.Time.ShouldBe(5);
            }
        }
    }
}
