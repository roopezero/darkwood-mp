using DarkwoodMP.Net;

namespace DarkwoodMP.World.Dreaming
{
    /// The bytes of MsgType.DreamStart (host to client): str the level
    /// dream's preset name.
    public static class DreamStart
    {
        /// The payload, after Begin(MsgType.DreamStart).
        public static void Write(PacketWriter w, string dream)
        {
            w.WriteStr(dream);
        }

        /// The dream's preset name.
        public static string Read(PacketReader r)
        {
            return r.ReadStr();
        }
    }
}
