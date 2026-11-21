namespace DarkwoodMP.Combat.Damage
{
    /// One of the game's InvItemEffects as it travels with a hit
    /// (SentEffects): bleeding, poison and the like.
    public readonly struct SentEffect
    {
        /// CharacterEffectType, as its number.
        public int Type { get; init; }

        public float StartDelay { get; init; }

        public float Duration { get; init; }

        public float Modifier { get; init; }

        public float Interval { get; init; }

        public bool StopsBleeding { get; init; }

        public bool StopsPoison { get; init; }

        public bool PoisonOverlay { get; init; }

        /// activateSound: null for none, which is written as "".
        public string Sound { get; init; }
    }
}
