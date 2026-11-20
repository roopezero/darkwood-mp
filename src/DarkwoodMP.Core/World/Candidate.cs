namespace DarkwoodMP.World
{
    /// One possible copy: its squared distance, and whether it has the
    /// same place in its hierarchy (PlaceOf).
    public readonly struct Candidate
    {
        /// The squared distance on the ground (SquaredDistance).
        public float Squared { get; init; }

        public bool SamePlace { get; init; }
    }
}
