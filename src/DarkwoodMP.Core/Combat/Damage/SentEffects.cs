using System.Collections.Generic;
using DarkwoodMP.Net;

namespace DarkwoodMP.Combat.Damage
{
    /// The bytes of a hit's effects, in PlayerDamage, EnemyHit and NpcHit:
    /// u8 n, n x effect: i32 type, f32 startDelay, f32 duration,
    /// f32 modifier, f32 interval, u8 flags (1 stopsBleeding, 2 stopsPoison,
    /// 4 hasPoisonOverlay), str activateSound ("" none). At most
    /// DamageRules.MostEffects are written.
    public static class SentEffects
    {
        public const byte StopsBleeding = 1;
        public const byte StopsPoison = 2;
        public const byte PoisonOverlay = 4;

        /// Writes the first MostEffects of them; none for null.
        public static void Write(
            PacketWriter w, IReadOnlyList<SentEffect> effects)
        {
            int count = 0;
            if (effects != null)
            {
                count = effects.Count;
            }

            int n = DamageRules.EffectsSent(count);
            w.W.Write((byte)n);
            for (int i = 0; i < n; i++)
            {
                WriteOne(w, effects[i]);
            }
        }

        private static void WriteOne(PacketWriter w, in SentEffect e)
        {
            w.W.Write(e.Type);
            w.W.Write(e.StartDelay);
            w.W.Write(e.Duration);
            w.W.Write(e.Modifier);
            w.W.Write(e.Interval);
            int flags = 0;
            if (e.StopsBleeding)
            {
                flags |= StopsBleeding;
            }

            if (e.StopsPoison)
            {
                flags |= StopsPoison;
            }

            if (e.PoisonOverlay)
            {
                flags |= PoisonOverlay;
            }

            w.W.Write((byte)flags);
            string sound = e.Sound;
            if (sound == null)
            {
                sound = "";
            }

            w.WriteStr(sound);
        }

        /// Reads them all, however many were written; an empty sound is
        /// read as null.
        public static List<SentEffect> Read(PacketReader r)
        {
            int n = r.R.ReadByte();
            var effects = new List<SentEffect>();
            for (int i = 0; i < n; i++)
            {
                effects.Add(ReadOne(r));
            }

            return effects;
        }

        private static SentEffect ReadOne(PacketReader r)
        {
            int type = r.R.ReadInt32();
            float startDelay = r.R.ReadSingle();
            float duration = r.R.ReadSingle();
            float modifier = r.R.ReadSingle();
            float interval = r.R.ReadSingle();
            byte flags = r.R.ReadByte();
            string sound = r.ReadStr();
            if (sound.Length == 0)
            {
                sound = null;
            }

            return new SentEffect
            {
                Type = type,
                StartDelay = startDelay,
                Duration = duration,
                Modifier = modifier,
                Interval = interval,
                StopsBleeding = (flags & StopsBleeding) != 0,
                StopsPoison = (flags & StopsPoison) != 0,
                PoisonOverlay = (flags & PoisonOverlay) != 0,
                Sound = sound,
            };
        }
    }
}
