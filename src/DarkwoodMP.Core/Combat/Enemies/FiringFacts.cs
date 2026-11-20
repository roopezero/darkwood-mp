namespace DarkwoodMP.Combat.Enemies
{
    /// The host's numbers for the weapon a client's hit came with (the
    /// InvItemClass NetEnemies.Weapon reads from the hit's JSON, and its
    /// baseClass), as the hit rate needs them. Zero is bare hands.
    public readonly struct FiringFacts
    {
        /// The hit named a weapon with a class (Weapon(json) isn't null).
        public bool Armed { get; init; }

        /// InvItem.isMelee.
        public bool Melee { get; init; }

        /// InvItem.isFirearm.
        public bool Firearm { get; init; }

        /// InvItem.fireMode is fullAuto or burst.
        public bool Auto { get; init; }

        /// InvItem.fireRate: seconds between shots.
        public float FireRate { get; init; }

        /// InvItem.projectileAmount: pellets a shot.
        public int Projectiles { get; init; }

        /// InvItemClass.type, for the refusal's words; null for bare
        /// hands.
        public string Type { get; init; }
    }
}
