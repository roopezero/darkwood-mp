using DarkwoodMP.Net;

namespace DarkwoodMP.Combat.Damage
{
    /// The bytes of MsgType.EnemyExplosion (host -> clients): vec3
    /// position, str sound, str effect.
    public static class EnemyExplosionMessage
    {
        /// The payload, after Begin(MsgType.EnemyExplosion).
        public static void Write(PacketWriter w, in EnemyExplosion blast)
        {
            w.Write(blast.At);
            w.WriteStr(blast.Sound);
            w.WriteStr(blast.Effect);
        }

        public static EnemyExplosion Read(PacketReader r)
        {
            Vec3 at = r.ReadVec3();
            string sound = r.ReadStr();
            string effect = r.ReadStr();
            return new EnemyExplosion
            {
                At = at,
                Sound = sound,
                Effect = effect,
            };
        }
    }
}
