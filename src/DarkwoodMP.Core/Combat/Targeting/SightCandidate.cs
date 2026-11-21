namespace DarkwoodMP.Combat.Targeting
{
    /// A living player in an enemy's view-range list, as the sight check's
    /// ordering sees them.
    public readonly struct SightCandidate
    {
        /// Where the shell found them: the index in the view-range list.
        public int Slot { get; init; }

        /// The enemy's stored threat for them, after the decay
        /// (ThreatTable.Get).
        public float Stored { get; init; }

        /// From the enemy, in 3D (Vector3.Distance: height counts).
        public float Distance { get; init; }

        /// Their transform is the enemy's target.
        public bool Current { get; init; }

        /// Set by ThreatRules.Order: Stored plus closeness.
        public float Score { get; init; }
    }
}
