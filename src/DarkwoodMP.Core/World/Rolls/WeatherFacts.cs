namespace DarkwoodMP.World.Rolls
{
    /// The day's rain and fog plan (the game's Rain), as the host sends it
    /// (WeatherKey.Of).
    public readonly struct WeatherFacts
    {
        /// Rain.rainToday.
        public bool RainToday { get; init; }

        /// Rain.timeToStart.
        public float Start { get; init; }

        /// Rain.duration.
        public float Duration { get; init; }

        /// Rain.preRainLightning.
        public bool Lightning { get; init; }

        /// Rain.preRainLightningTime.
        public float LightningStart { get; init; }

        /// Rain.lightningTime.
        public float LightningFor { get; init; }

        /// Rain.timeToFadeInFog.
        public FogTime FogIn { get; init; }

        /// Rain.timeToFadeOutFog.
        public FogTime FogOut { get; init; }
    }
}
