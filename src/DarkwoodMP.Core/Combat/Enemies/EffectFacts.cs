namespace DarkwoodMP.Combat.Enemies
{
    /// The effects of a client's hit and of the weapon it came with (the
    /// plugin's NetEnemies.WeaponEffects). Effect types are
    /// InvItemEffect.type as numbers.
    public readonly struct EffectFacts
    {
        /// The hit named a weapon with a class.
        public bool Armed { get; init; }

        /// The types of the effects the class's melee sensors and flames
        /// cause, in the order the shell found them.
        public int[] Own { get; init; }

        /// The types of the effects the hit was sent with, in order.
        public int[] Sent { get; init; }
    }
}
