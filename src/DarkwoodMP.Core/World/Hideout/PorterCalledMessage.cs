using DarkwoodMP.Net;

namespace DarkwoodMP.World.Hideout
{
    /// The bytes of MsgType.PorterCalled (either way): vec3 the porter
    /// place's position.
    public static class PorterCalledMessage
    {
        /// The payload, after Begin(MsgType.PorterCalled).
        public static void Write(PacketWriter w, Vec3 place)
        {
            w.Write(place);
        }

        public static Vec3 Read(PacketReader r)
        {
            return r.ReadVec3();
        }
    }
}
