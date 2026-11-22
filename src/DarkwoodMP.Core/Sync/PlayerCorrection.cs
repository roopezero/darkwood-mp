using DarkwoodMP.Net;

namespace DarkwoodMP.Sync
{
    /// The bytes of MsgType.PlayerCorrection (host to client): vec3, where
    /// the host put the client's player back after it moved too fast.
    public static class PlayerCorrection
    {
        /// The payload, after Begin(MsgType.PlayerCorrection).
        public static void Write(PacketWriter w, Vec3 accepted)
        {
            w.Write(accepted);
        }

        /// Where the host put our player back.
        public static Vec3 Read(PacketReader r)
        {
            return r.ReadVec3();
        }
    }
}
