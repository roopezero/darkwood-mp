using System;
using System.Collections.Generic;

namespace DarkwoodMP.Combat.Targeting
{
    /// One enemy's threat for each player (the plugin's Threat.Table).
    /// The key is the player's character object; the shell says which
    /// keys are gone (a destroyed Unity object), and a gone key counts as
    /// none and is dropped at the next decay. The decay is lazy: Get
    /// returns the value as of the last decay.
    public sealed class ThreatTable<TKey>
        where TKey : class
    {
        readonly Dictionary<TKey, float> _values =
            new Dictionary<TKey, float>();

        readonly List<TKey> _keys = new List<TKey>();

        readonly Func<TKey, bool> _gone;

        /// When the table first exists, for this enemy.
        public ThreatTable(Seconds now, Func<TKey, bool> gone)
        {
            _gone = gone;
            LastDecay = now;
            LastSeen = now;
        }

        /// When the values last decayed.
        public Seconds LastDecay { get; private set; }

        /// When the last seen gain ran (ThreatRules.SeenSpan).
        public Seconds LastSeen { get; private set; }

        /// Each player's stored threat as of the last decay.
        public IReadOnlyDictionary<TKey, float> Values => _values;

        /// Fades every value by the time since the last decay
        /// (ThreatRules.HalfLife), forgetting gone keys and values that
        /// fall below ThreatRules.MinStored. Nothing happens when no time
        /// has passed, or the clock went back.
        public void Decay(Seconds now)
        {
            float dt = now.Value - LastDecay.Value;
            if (dt <= 0f)
            {
                return;
            }

            LastDecay = now;
            float k = (float)Math.Pow(0.5f, dt / ThreatRules.HalfLife);
            _keys.Clear();
            _keys.AddRange(_values.Keys);
            for (int i = 0; i < _keys.Count; i++)
            {
                var c = _keys[i];
                if (_gone(c))
                {
                    _values.Remove(c);
                    continue;
                }

                float v = _values[c] * k;
                if (v < ThreatRules.MinStored)
                {
                    _values.Remove(c);
                }
                else
                {
                    _values[c] = v;
                }
            }

            _keys.Clear();
        }

        /// Decays, then adds to a player's threat. Nothing at all, not
        /// even the decay, for a gone key or an amount of 0 or less.
        public void Add(TKey who, float amount, Seconds now)
        {
            if (_gone(who) || amount <= 0f)
            {
                return;
            }

            Decay(now);
            _values.TryGetValue(who, out float v);
            _values[who] = v + amount;
        }

        /// A player's stored threat as of the last decay; 0 for one with
        /// none or a gone key.
        public float Get(TKey who)
        {
            if (!_gone(who) && _values.TryGetValue(who, out float v))
            {
                return v;
            }

            return 0f;
        }

        /// The span the seen gain covers now (ThreatRules.SeenSpan), and
        /// starts the next one: LastSeen moves on even when the enemy saw
        /// no player.
        public float TakeSeenSpan(Seconds now)
        {
            float span = ThreatRules.SeenSpan(now, LastSeen);
            LastSeen = now;
            return span;
        }
    }
}
