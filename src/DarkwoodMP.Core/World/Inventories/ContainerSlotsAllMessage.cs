using DarkwoodMP.Net;

namespace DarkwoodMP.World.Inventories
{
    /// The bytes of MsgType.ContainerSlotsAll (host to client): ObjRef,
    /// bigstr contents (Inventory.SaveState as JSON, "" when the host
    /// hasn't got the container).
    public static class ContainerSlotsAllMessage
    {
        /// The payload, after Begin(MsgType.ContainerSlotsAll).
        public static void Write(PacketWriter w, ObjName box, string contents)
        {
            box.Write(w);
            w.WriteBigStr(contents);
        }

        /// The container, and its contents as they came.
        public static ObjName Read(PacketReader r, out string contents)
        {
            var box = ObjName.Read(r);
            contents = r.ReadBigStr();
            return box;
        }
    }
}
