using DarkwoodMP.Net;

namespace DarkwoodMP.World.Inventories
{
    /// The bytes of MsgType.StorageSort (client to host, inside a
    /// Request): ObjRef of the workbench's storage whose Sort & Stack the
    /// client's player pressed (SlotShare.SortPressed and OnSort).
    public static class StorageSortMessage
    {
        /// The payload, after Begin(MsgType.StorageSort).
        public static void Write(PacketWriter w, ObjName storage)
        {
            storage.Write(w);
        }

        /// The storage to sort.
        public static ObjName Read(PacketReader r)
        {
            return ObjName.Read(r);
        }
    }
}
