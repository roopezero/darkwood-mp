using DarkwoodMP.Net;

namespace DarkwoodMP.Combat.Landings
{
    /// The bytes of MsgType.EnemyProjectile (host -> clients): u32 net id,
    /// str projectile, vec3 position, f32 yaw, vec3 velocity.
    public static class EnemyProjectileMessage
    {
        /// The payload, after Begin(MsgType.EnemyProjectile). Also the start
        /// of EnemySensorThrown's.
        public static void Write(PacketWriter w, in EnemyThrow thrown)
        {
            w.W.Write(thrown.By);
            w.WriteStr(thrown.Name);
            w.Write(thrown.At);
            w.W.Write(thrown.Yaw);
            w.Write(thrown.Velocity);
        }

        public static EnemyThrow Read(PacketReader r)
        {
            uint by = r.R.ReadUInt32();
            string name = r.ReadStr();
            Vec3 at = r.ReadVec3();
            float yaw = r.R.ReadSingle();
            Vec3 velocity = r.ReadVec3();
            return new EnemyThrow
            {
                By = by,
                Name = name,
                At = at,
                Yaw = yaw,
                Velocity = velocity,
            };
        }
    }
}
