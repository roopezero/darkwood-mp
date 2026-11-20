namespace DarkwoodMP.Combat.Damage
{
    /// The host's numbers for the weapon a client names in a hit
    /// (NetEnemies.Weapon's InvItemClass). Zero is bare hands.
    public readonly struct WeaponFacts
    {
        /// The weapon has a class: weapon?.baseClass != null. False for
        /// bare hands, "" or bad JSON, or a type the host doesn't know.
        public bool HasClass { get; init; }

        /// baseClass.damage.
        public int Damage { get; init; }

        /// baseClass.specialDamage.
        public int SpecialDamage { get; init; }

        /// Every baseClass.upgrades[i].damageModifier, skipping null
        /// upgrades: all the upgrades the class offers, not the ones the
        /// item has. Null when upgrades is null.
        public float[] UpgradeModifiers { get; init; }
    }
}
