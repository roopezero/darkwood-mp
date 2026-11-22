namespace DarkwoodMP.Sync.Players
{
    /// Where between its two snapshots a puppet is drawn.
    public readonly struct PuppetBlend
    {
        /// Lerp the position and Slerp the rotation from the previous
        /// snapshot to the last by Fraction. When false, the last snapshot
        /// is drawn as it is.
        public bool Blends { get; init; }

        /// 0 to 1.
        public float Fraction { get; init; }
    }
}
