namespace DarkwoodMP.Sync.Players
{
    /// Our player's light dot, which a lantern on the hotbar widens and
    /// makes a logic light (PlayerCapture.Capture).
    public readonly struct LanternFacts
    {
        /// Player.lightDot isn't null.
        public bool HasDot { get; init; }

        /// lightDot.lightsPlayer.
        public bool LightsPlayer { get; init; }

        /// Singleton<Controller>.Instance isn't null.
        public bool HasController { get; init; }

        /// Controller.logicLights holds the light dot.
        public bool IsLogicLight { get; init; }

        /// lightDot.LightRadius.
        public float Radius { get; init; }
    }
}
