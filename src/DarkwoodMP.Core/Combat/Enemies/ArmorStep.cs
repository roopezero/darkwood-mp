namespace DarkwoodMP.Combat.Enemies
{
    /// What a copy's shadow armour does to match the host's
    /// (CopyRules.Armor).
    public enum ArmorStep
    {
        /// Nothing.
        None = 0,

        /// A ShadowArmor is added (its Start makes the effect).
        Add,

        /// Not started yet, so no effect to stop: the component goes.
        Destroy,

        /// ShadowArmor.die, at once unless it fades.
        Die,
    }
}
