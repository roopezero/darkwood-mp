using DarkwoodMP.Net;

namespace DarkwoodMP.World.Dragging
{
    /// The bytes of MsgType.DragOwner (host to clients): ObjRef, u64 owner
    /// (0: nobody), u8 flags (1: let go if you aren't the owner).
    public static class DragOwner
    {
        /// The payload, after Begin(MsgType.DragOwner).
        public static void Write(PacketWriter w, in OwnerWord word)
        {
            word.Name.Write(w);
            w.W.Write(word.Owner);
            byte flags = 0;
            if (word.LetGo)
            {
                flags = 1;
            }

            w.W.Write(flags);
        }

        /// The owner, as written.
        public static OwnerWord Read(PacketReader r)
        {
            var name = ObjName.Read(r);
            ulong owner = r.R.ReadUInt64();
            bool letGo = (r.R.ReadByte() & 1) != 0;
            return new OwnerWord
            {
                Name = name,
                Owner = owner,
                LetGo = letGo,
            };
        }
    }
}
