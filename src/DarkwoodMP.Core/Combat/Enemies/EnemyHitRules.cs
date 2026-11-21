using System;

namespace DarkwoodMP.Combat.Enemies
{
    /// The host's checks of a client's hit on one of its enemies (the
    /// plugin's NetEnemies.OnHit, ApplyHit, MaxHits and WeaponEffects).
    /// The damage cap is DamageRules' (step 6.3); the reach check is
    /// ReachRules.InReach with the reach from ReachFor; the hit rate is
    /// HitRate's.
    public static class EnemyHitRules
    {
        /// How far a melee hit reaches (generous: the positions the host
        /// has are a little late).
        public const float MeleeReach = 500f;

        /// Most hits a player lands in a second with a melee weapon or
        /// bare hands (or a slow gun).
        public const int MaxHitsPerSecond = 24;

        /// The shortest gap between shots counted for automatic and burst
        /// fire.
        public const float AutoGap = 0.05f;

        /// The shortest gap between shots counted for a gun fired one
        /// shot a click.
        public const float ClickGap = 0.15f;

        /// Half again as a margin for hits that arrive bunched up.
        public const float RateMargin = 1.5f;

        /// The checks in OnHit, before ApplyHit: the enemy, then the
        /// stand-in.
        public static Verdict MayHit(in HitFacts hit)
        {
            if (!hit.Tracked || !hit.InPlay)
            {
                return Verdict.Refuse(Refusal.EnemyNotAlive);
            }

            if (!hit.StandIn)
            {
                return Verdict.Refuse(Refusal.NoStandIn);
            }

            return Verdict.Allow;
        }

        /// How far from the enemy the hitter may be: a weapon that isn't
        /// melee reaches as far as enemies are sent, bare hands and melee
        /// MeleeReach.
        public static float ReachFor(in FiringFacts weapon)
        {
            if (weapon.Armed && !weapon.Melee)
            {
                return EnemyRelayRules.RelevantRange;
            }

            return MeleeReach;
        }

        /// Most hits a second this weapon lands: each of a gun's shots
        /// sends a hit for every pellet, at most one shot per fireRate
        /// (AutoGap or ClickGap at the least), with RateMargin. Never under
        /// MaxHitsPerSecond.
        public static int MaxHits(in FiringFacts weapon)
        {
            if (!weapon.Armed || !weapon.Firearm)
            {
                return MaxHitsPerSecond;
            }

            float gap = ClickGap;
            if (weapon.Auto)
            {
                gap = AutoGap;
            }

            float shots = 1f / Max(weapon.FireRate, gap);
            int pellets = weapon.Projectiles;
            if (pellets < 1)
            {
                pellets = 1;
            }

            int most = (int)Math.Ceiling(pellets * shots * RateMargin);
            if (most > MaxHitsPerSecond)
            {
                return most;
            }

            return MaxHitsPerSecond;
        }

        /// The capped damage is applied (getHit runs): more than 0. NaN
        /// isn't.
        public static bool Lands(float applied)
        {
            return applied > 0f;
        }

        /// Which of the weapon's own effects a hit's sent effects become:
        /// for each sent one, the index of the first own effect of its
        /// type not used yet, or -1 when the weapon doesn't cause it
        /// (dropped and logged). None for bare hands or no sent effects.
        public static int[] Effects(in EffectFacts hit)
        {
            if (!hit.Armed || hit.Sent == null || hit.Sent.Length == 0)
            {
                return new int[0];
            }

            var used = new bool[hit.Own.Length];
            var result = new int[hit.Sent.Length];
            for (int i = 0; i < hit.Sent.Length; i++)
            {
                result[i] = FirstUnused(hit.Own, used, hit.Sent[i]);
                if (result[i] >= 0)
                {
                    used[result[i]] = true;
                }
            }

            return result;
        }

        static int FirstUnused(int[] own, bool[] used, int type)
        {
            for (int i = 0; i < own.Length; i++)
            {
                if (!used[i] && own[i] == type)
                {
                    return i;
                }
            }

            return -1;
        }

        /// As Mathf.Max: a unless b is larger.
        static float Max(float a, float b)
        {
            if (a > b)
            {
                return a;
            }

            return b;
        }
    }
}
