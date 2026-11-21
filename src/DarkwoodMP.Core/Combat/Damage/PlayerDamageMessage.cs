using DarkwoodMP.Net;

namespace DarkwoodMP.Combat.Damage
{
    /// The bytes of MsgType.PlayerDamage (host -> client): f32 damage,
    /// u8 flags (1: shadow), str attacker, then the effects (SentEffects).
    public static class PlayerDamageMessage
    {
        public const byte Shadow = 1;

        /// The payload, after Begin(MsgType.PlayerDamage).
        public static void Write(PacketWriter w, in StandInHit hit)
        {
            w.W.Write(hit.Damage);
            int flags = 0;
            if (hit.Shadow)
            {
                flags = Shadow;
            }

            w.W.Write((byte)flags);
            w.WriteStr(hit.Attacker);
            SentEffects.Write(w, hit.Effects);
        }

        public static StandInHit Read(PacketReader r)
        {
            float damage = r.R.ReadSingle();
            bool shadow = (r.R.ReadByte() & Shadow) != 0;
            string attacker = r.ReadStr();
            var effects = SentEffects.Read(r);
            return new StandInHit
            {
                Damage = damage,
                Shadow = shadow,
                Attacker = attacker,
                Effects = effects,
            };
        }
    }
}
