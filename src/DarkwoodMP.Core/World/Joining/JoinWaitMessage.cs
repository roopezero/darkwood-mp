using DarkwoodMP.Net;

namespace DarkwoodMP.World.Joining
{
    /// The bytes of MsgType.JoinWait (host to client): str why. Named
    /// apart from the JoinWait reasons.
    public static class JoinWaitMessage
    {
        /// The payload, after Begin(MsgType.JoinWait).
        public static void Write(PacketWriter w, string why)
        {
            w.WriteStr(why);
        }

        public static string Read(PacketReader r)
        {
            return r.ReadStr();
        }
    }
}
