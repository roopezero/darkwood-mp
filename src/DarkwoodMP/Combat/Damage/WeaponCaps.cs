using System.Collections.Generic;

namespace DarkwoodMP.Combat.Damage
{
    /// <summary>
    /// Host: the most damage a client's hit with a weapon may do to one of
    /// the host's enemies (WeaponRules.DamageCap), from the game's own
    /// numbers for the weapon's class.
    /// </summary>
    internal static class WeaponCaps
    {
        /// <summary>
        /// The cap for this weapon; bare hands for null.
        /// </summary>
        public static float Of(InvItemClass weapon) =>
            WeaponRules.DamageCap(FactsOf(weapon));

        /// <summary>
        /// The weapon's class's damage and the damage modifiers of every
        /// upgrade its class offers (InvItem.upgrades; not the upgrades the
        /// hit says it has, which the client sends): the only code reading
        /// them for WeaponRules.DamageCap.
        /// </summary>
        public static WeaponFacts FactsOf(InvItemClass weapon)
        {
            InvItem b = null;
            if (weapon != null)
            {
                b = weapon.baseClass;
            }

            if (b == null)
            {
                return new WeaponFacts();
            }

            float[] mods = null;
            if (b.upgrades != null)
            {
                var found = new List<float>();
                foreach (var u in b.upgrades)
                {
                    if (u != null)
                    {
                        found.Add(u.damageModifier);
                    }
                }

                mods = found.ToArray();
            }

            return new WeaponFacts
            {
                HasClass = true,
                Damage = b.damage,
                SpecialDamage = b.specialDamage,
                UpgradeModifiers = mods,
            };
        }
    }
}
