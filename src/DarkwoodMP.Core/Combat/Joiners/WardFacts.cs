namespace DarkwoodMP.Combat.Joiners
{
    /// Whether an enemy's target has an effect (Wards.HasEffect).
    public readonly struct WardFacts
    {
        /// The target is a stand-in (WardRules.TargetsStandIn).
        public bool StandIn { get; init; }

        /// The effect type asked about.
        public WardKind Kind { get; init; }

        /// NetPlayerProxy.ShadowWard, from its player's state.
        public bool ShadowWard { get; init; }

        /// NetPlayerProxy.ForestSpiritWard.
        public bool ForestSpiritWard { get; init; }

        /// The host's player exists, has effects and has this type
        /// (effects.hasEffectType). Read only when the target isn't a
        /// stand-in.
        public bool HostHas { get; init; }
    }
}
