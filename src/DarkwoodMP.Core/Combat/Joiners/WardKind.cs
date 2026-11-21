namespace DarkwoodMP.Combat.Joiners
{
    /// The effect type the game's code asks the enemy's target about
    /// (CharacterEffectType), as far as a stand-in knows it.
    public enum WardKind
    {
        /// Any type other than the two wards.
        Other,

        /// CharacterEffectType.shadowWard.
        ShadowWard,

        /// CharacterEffectType.forestSpiritWard.
        ForestSpiritWard,
    }
}
