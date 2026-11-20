namespace DarkwoodMP.Combat.Noise
{
    /// One of the game's own-player noise makers starting
    /// (PlayerNoiseSourcePatch.Prefix).
    public readonly struct OwnNoiseFacts
    {
        /// Player.Instance isn't null.
        public bool Player { get; init; }

        /// Which method it is (from __instance's type).
        public NoiseMaker Maker { get; init; }

        /// A door's opener argument (at NoiseRules.OpenerAt) is a
        /// Transform that isn't null and has a Player.
        public bool PlayerOpener { get; init; }

        /// A sensor's type is MeleeSensorType.player.
        public bool PlayerSensor { get; init; }
    }
}
