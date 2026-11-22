namespace DarkwoodMP.Combat.Damage
{
    /// A hit on a character in the host's game
    /// (CharacterGetHitThreat.Postfix).
    public readonly struct BloodFacts
    {
        /// getHit's normalHit.
        public bool NormalHit { get; init; }

        /// The character's health before getHit (the prefix's __state).
        public float Before { get; init; }

        /// Its health after.
        public float After { get; init; }

        /// isNightTrader.
        public bool NightTrader { get; init; }

        /// ModHost.Session is set and IsHost.
        public bool Host { get; init; }
    }
}
