using System.Collections.Generic;
using DarkwoodMP.Net;

namespace DarkwoodMP.World.Inventories
{
    /// The bytes of MsgType.ContainerSlots (either way): ObjRef, u8 n, n x
    /// (u16 slot, str what it held (its key), str what it holds now (JSON)).
    public static class ContainerSlotsMessage
    {
        /// The most changes one message holds (its count is a byte).
        public const int MaxChanges = 200;

        /// The payload, after Begin(MsgType.ContainerSlots). The caller
        /// gives at most MaxChanges changes.
        public static void Write(
            PacketWriter w, ObjName box, IReadOnlyList<SlotChange> changes)
        {
            box.Write(w);
            w.W.Write((byte)changes.Count);
            foreach (var change in changes)
            {
                w.W.Write((ushort)change.Slot);
                w.WriteStr(change.Old);
                w.WriteStr(change.New);
            }
        }

        /// The container, and its changes in the order sent.
        public static ObjName Read(
            PacketReader r, out List<SlotChange> changes)
        {
            var box = ObjName.Read(r);
            int n = r.R.ReadByte();
            changes = new List<SlotChange>(n);
            for (int k = 0; k < n; k++)
            {
                changes.Add(new SlotChange
                {
                    Slot = r.R.ReadUInt16(),
                    Old = r.ReadStr(),
                    New = r.ReadStr(),
                });
            }

            return box;
        }
    }
}
