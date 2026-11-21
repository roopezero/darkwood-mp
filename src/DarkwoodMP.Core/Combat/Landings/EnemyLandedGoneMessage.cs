using DarkwoodMP.Net;

namespace DarkwoodMP.Combat.Landings
{
    /// The bytes of MsgType.EnemyLandedGone (host -> clients): i64 spawned
    /// id.
    public static class EnemyLandedGoneMessage
    {
        /// The payload, after Begin(MsgType.EnemyLandedGone).
        public static void Write(PacketWriter w, long id)
        {
            w.W.Write(id);
        }

        public static long Read(PacketReader r)
        {
            return r.R.ReadInt64();
        }
    }
}
