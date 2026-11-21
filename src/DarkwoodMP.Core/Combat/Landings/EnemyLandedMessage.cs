using DarkwoodMP.Net;

namespace DarkwoodMP.Combat.Landings
{
    /// The bytes of MsgType.EnemyLanded (host -> clients): i64 spawned id,
    /// u32 net id, str name, vec3 position, f32 yaw.
    public static class EnemyLandedMessage
    {
        /// The payload, after Begin(MsgType.EnemyLanded).
        public static void Write(PacketWriter w, in EnemyLanding landed)
        {
            w.W.Write(landed.Id);
            w.W.Write(landed.By);
            w.WriteStr(landed.Name);
            w.Write(landed.At);
            w.W.Write(landed.Yaw);
        }

        public static EnemyLanding Read(PacketReader r)
        {
            long id = r.R.ReadInt64();
            uint by = r.R.ReadUInt32();
            string name = r.ReadStr();
            Vec3 at = r.ReadVec3();
            float yaw = r.R.ReadSingle();
            return new EnemyLanding
            {
                Id = id,
                By = by,
                Name = name,
                At = at,
                Yaw = yaw,
            };
        }
    }
}
