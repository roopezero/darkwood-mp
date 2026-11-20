namespace DarkwoodMP.Combat.Beams
{
    /// Where a position is in a puppet's flashlight beam, on the ground.
    public readonly struct BeamSpot
    {
        /// Inside the beam.
        public bool In { get; init; }

        /// How far ahead of the puppet along where it faces; 0 when it
        /// faces nowhere on the ground.
        public float Ahead { get; init; }

        /// How far to the side of that line; 0 the same way.
        public float Aside { get; init; }
    }
}
