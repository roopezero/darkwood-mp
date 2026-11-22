using DarkwoodMP.Net;

namespace DarkwoodMP.World.Inventories
{
    /// The bytes of MsgType.ContainerState (either way): ObjRef, bigstr
    /// contents (Inventory.SaveState as JSON) of a container.
    public static class ContainerStateMessage
    {
        /// The payload, after Begin(MsgType.ContainerState).
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
