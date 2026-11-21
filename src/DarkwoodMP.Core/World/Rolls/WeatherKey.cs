namespace DarkwoodMP.World.Rolls
{
    /// The weather as text, which the host compares with what it last sent
    /// to see whether it changed (and logs). Only compared within one game,
    /// so the current culture's digits and Mono's way of rounding a small
    /// negative number don't matter.
    public static class WeatherKey
    {
        /// "rain 1 at 300 for 120, lightning 0 0 0, fog 3/400-none": the
        /// times rounded to whole numbers ("0"), the fog's as day/time.
        public static string Of(in WeatherFacts rain)
        {
            return "rain " + Bit(rain.RainToday)
                + " at " + rain.Start.ToString("0")
                + " for " + rain.Duration.ToString("0")
                + ", lightning " + Bit(rain.Lightning)
                + " " + rain.LightningStart.ToString("0")
                + " " + rain.LightningFor.ToString("0")
                + ", fog " + Fog(rain.FogIn) + "-" + Fog(rain.FogOut);
        }

        static string Bit(bool on)
        {
            if (on)
            {
                return "1";
            }

            return "0";
        }

        static string Fog(FogTime time)
        {
            if (!time.Set)
            {
                return "none";
            }

            return time.Day + "/" + time.Time;
        }
    }
}
