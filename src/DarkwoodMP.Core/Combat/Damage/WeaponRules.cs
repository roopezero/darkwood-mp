namespace DarkwoodMP.Combat.Damage
{
    /// Host: the most damage a client's hit on one of the host's enemies
    /// may do (NetEnemies.DamageCap and its use in NetEnemies.ApplyHit). A
    /// larger hit is cut down to the cap, never refused.
    public static class WeaponRules
    {
        /// Bare hands: as the game's weakest weapons.
        public const float BareHandsCap = 40f;

        /// No weapon's cap is under this.
        public const float LeastCap = 10f;

        /// The Strong skill doubles a weapon's damage.
        public const float StrongSkill = 2f;

        /// A margin over the most the weapon can do.
        public const float Margin = 1.25f;

        /// The weapon's damage or special damage, the larger, with every
        /// damage upgrade its class offers added up as getModdedDamage
        /// does (each modifier over 1 adds what it is over 1), times the
        /// Strong skill and the margin; at least LeastCap.
        public static float DamageCap(in WeaponFacts weapon)
        {
            if (!weapon.HasClass)
            {
                return BareHandsCap;
            }

            float mod = 1f;
            var mods = weapon.UpgradeModifiers;
            if (mods != null)
            {
                for (int i = 0; i < mods.Length; i++)
                {
                    if (mods[i] > 1f)
                    {
                        mod += mods[i] - 1f;
                    }
                }
            }

            int most = Floats.Max(weapon.Damage, weapon.SpecialDamage);
            return Floats.Max(LeastCap, most * mod * StrongSkill * Margin);
        }

        /// The damage the host applies: the hit between 0 and the cap, as
        /// Mathf.Clamp (NaN stays NaN, so no getHit runs for it).
        public static float Applied(float damage, float cap) =>
            Floats.Clamp(damage, 0f, cap);

        /// Whether the host logs that the hit was more than the weapon
        /// does: the applied damage is less than the hit.
        public static bool Cut(float damage, float applied) =>
            applied < damage;
    }
}
