using DarkwoodMP.Net;

namespace DarkwoodMP.World.Infections
{
    /// The bytes of MsgType.InfectionSpawned (host -> client): i64 spawned
    /// id, f32 x, f32 y, f32 z, ObjRef parent.
    public static class InfectionSpawned
    {
        /// The payload, after Begin(MsgType.InfectionSpawned).
        public static void Write(PacketWriter w, in SpawnedPatch patch)
        {
            w.W.Write(patch.Id);
            w.Write(patch.At);
            patch.Parent.Write(w);
        }

        public static SpawnedPatch Read(PacketReader r)
        {
            long id = r.R.ReadInt64();
            Vec3 at = r.ReadVec3();
            ObjName parent = ObjName.Read(r);
            return new SpawnedPatch
            {
                Id = id,
                At = at,
                Parent = parent,
            };
        }
    }
}
