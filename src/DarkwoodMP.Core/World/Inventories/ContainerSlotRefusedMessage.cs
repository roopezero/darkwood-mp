using DarkwoodMP.Net;

namespace DarkwoodMP.World.Inventories
{
    /// The bytes of MsgType.ContainerSlotRefused (host to client): ObjRef,
    /// u16 slot, str what it holds (JSON), str what you saw there (key),
    /// str what you put there (JSON).
    public static class ContainerSlotRefusedMessage
    {
        /// The payload, after Begin(MsgType.ContainerSlotRefused).
        public static void Write(PacketWriter w, ObjName box, RefusedSlot slot)
        {
            box.Write(w);
            w.W.Write((ushort)slot.Slot);
            w.WriteStr(slot.Held);
            w.WriteStr(slot.Old);
            w.WriteStr(slot.Tried);
        }

        /// The container, and the refused change.
        public static ObjName Read(PacketReader r, out RefusedSlot slot)
        {
            var box = ObjName.Read(r);
            slot = new RefusedSlot
            {
                Slot = r.R.ReadUInt16(),
                Held = r.ReadStr(),
                Old = r.ReadStr(),
                Tried = r.ReadStr(),
            };

            return box;
        }
    }
}
