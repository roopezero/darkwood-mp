namespace DarkwoodMP.Combat.Noise
{
    /// A noise a client's player made, as PlayerNoise carries it to the
    /// host (the arguments of Character.alertInArea).
    public readonly struct SentNoise
    {
        /// Where the noise is.
        public Vec3 At { get; init; }
        /// How far it carries.
        public float Range { get; init; }
        /// How loud it is.
        public float Volume { get; init; }
        /// The game's dangerousSound.
        public bool Dangerous { get; init; }
        /// A gunshot.
        public bool Gunshot { get; init; }
    }
}
