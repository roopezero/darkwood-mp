using DarkwoodMP.Net;

namespace DarkwoodMP.World.Inventories
{
    /// The bytes of MsgType.ContainerSlotsAsk (client to host): ObjRef of
    /// the container whose contents the client wants.
    public static class ContainerSlotsAskMessage
    {
        /// The payload, after Begin(MsgType.ContainerSlotsAsk).
        public static void Write(PacketWriter w, ObjName box)
        {
            box.Write(w);
        }

        /// The container asked about.
        public static ObjName Read(PacketReader r)
        {
            return ObjName.Read(r);
        }
    }
}
