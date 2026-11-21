using DarkwoodMP.Net;

namespace DarkwoodMP.World.Inventories
{
    /// The bytes of MsgType.DeathBagMarker (either way): i64 spawned id of
    /// a death bag (ObjSpawn), vec3 where its map marker stands.
    public static class DeathBagMarkerMessage
    {
        /// The payload, after Begin(MsgType.DeathBagMarker).
        public static void Write(PacketWriter w, long bag, Vec3 marker)
        {
            w.W.Write(bag);
            w.Write(marker);
        }

        /// The bag's spawned id, and where its marker stands.
        public static long Read(PacketReader r, out Vec3 marker)
        {
            long bag = r.R.ReadInt64();
            marker = r.ReadVec3();
            return bag;
        }
    }
}
