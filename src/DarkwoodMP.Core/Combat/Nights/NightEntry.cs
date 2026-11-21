namespace DarkwoodMP.Combat.Nights
{
    /// One of the night scenario's attackers (a CharacterToSpawn), and how
    /// many of its kind are out at the hideout now.
    public readonly struct NightEntry
    {
        /// CharacterToSpawn.characterName; null for a null entry.
        public string Name { get; init; }

        /// How many may be out at once (CharacterToSpawn.amount).
        public int Amount { get; init; }

        /// How many of its kind are out at the hideout
        /// (HideoutBudgets.Out; 0 with no hideout).
        public int Out { get; init; }
    }
}
