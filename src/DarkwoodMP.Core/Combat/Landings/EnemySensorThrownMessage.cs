using DarkwoodMP.Net;

namespace DarkwoodMP.Combat.Landings
{
    /// The bytes of MsgType.EnemySensorThrown (host -> clients):
    /// EnemyProjectile's payload, then f32 land x, f32 land z.
    public static class EnemySensorThrownMessage
    {
        /// The payload, after Begin(MsgType.EnemySensorThrown).
        public static void Write(PacketWriter w, in EnemySensorThrow thrown)
        {
            EnemyProjectileMessage.Write(w, thrown.Throw);
            w.W.Write(thrown.Land.X);
            w.W.Write(thrown.Land.Z);
        }

        public static EnemySensorThrow Read(PacketReader r)
        {
            EnemyThrow thrown = EnemyProjectileMessage.Read(r);
            float x = r.R.ReadSingle();
            float z = r.R.ReadSingle();
            return new EnemySensorThrow
            {
                Throw = thrown,
                Land = new Vec2(x, z),
            };
        }
    }
}
