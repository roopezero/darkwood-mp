using DarkwoodMP.Net;

namespace DarkwoodMP.World.Inventories
{
    /// The bytes of MsgType.ContainerLock (client to host): ObjRef, u8
    /// open: this player opened (1) or closed (0) the container.
    public static class ContainerLockMessage
    {
        /// The payload, after Begin(MsgType.ContainerLock).
        public static void Write(PacketWriter w, ObjName box, bool open)
        {
            box.Write(w);
            byte flag = 0;
            if (open)
            {
                flag = 1;
            }

            w.W.Write(flag);
        }

        /// The container, and whether it was opened (any byte but 0).
        public static ObjName Read(PacketReader r, out bool open)
        {
            var box = ObjName.Read(r);
            open = r.R.ReadByte() != 0;
            return box;
        }
    }
}
