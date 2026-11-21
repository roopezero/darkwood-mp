using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP
{
    /// Unity's Vector3 on the wire, as the core's Vec3 (3 x f32).
    public static class PacketVectors
    {
        public static void Write(this PacketWriter w, Vector3 v)
        {
            w.Write(new Vec3(v.x, v.y, v.z));
        }

        public static Vector3 ReadVector3(this PacketReader r)
        {
            Vec3 v = r.ReadVec3();
            return new Vector3(v.X, v.Y, v.Z);
        }
    }
}
