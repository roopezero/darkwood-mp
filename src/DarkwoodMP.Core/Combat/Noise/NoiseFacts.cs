namespace DarkwoodMP.Combat.Noise
{
    /// A noise the game raises (PlayerNoisePatch.Prefix).
    public readonly struct NoiseFacts
    {
        /// Threat.NoiseSource isn't null.
        public bool Set { get; init; }

        /// PlayerNoisePatch.OwnNoise.
        public int OwnNoise { get; init; }

        /// PlayerNoisePatch.CalledFromPlayer() (the stack). The shell reads
        /// it only when Set is false and OwnNoise is 0 or less, as today.
        public bool FromPlayer { get; init; }

        /// Session.Instance is set, IsClient, and NetEnemies.ClientMode.
        public bool Client { get; init; }
    }
}
