using System.Collections.Generic;

namespace DarkwoodMP.Combat.Enemies
{
    /// The host's memory of the hits each client landed on its enemies in
    /// the last second (the plugin's NetEnemies._hitTimes and HitRateOk).
    /// One per session, on the host.
    public sealed class HitRate
    {
        /// The span hits are counted over.
        public const float Window = 1f;

        readonly Dictionary<ulong, Queue<float>> _times =
            new Dictionary<ulong, Queue<float>>();

        /// A hit from a player at now (Time.realtimeSinceStartup), after
        /// its reach was checked. Refused when the player already landed
        /// EnemyHitRules.MaxHits of this weapon in the window; a refused
        /// hit isn't counted. The detail is the weapon's type, or "bare
        /// hands".
        public Verdict Take(ulong from, Seconds now, in FiringFacts weapon)
        {
            if (!_times.TryGetValue(from, out var times))
            {
                times = new Queue<float>();
                _times[from] = times;
            }

            while (times.Count > 0 && now.Value - times.Peek() > Window)
            {
                times.Dequeue();
            }

            if (times.Count >= EnemyHitRules.MaxHits(weapon))
            {
                return Verdict.Refuse(Refusal.HitsTooFast, What(weapon));
            }

            times.Enqueue(now.Value);
            return Verdict.Allow;
        }

        static string What(in FiringFacts weapon)
        {
            if (weapon.Type == null)
            {
                return "bare hands";
            }

            return weapon.Type;
        }
    }
}
