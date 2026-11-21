using DarkwoodMP.Net;

namespace DarkwoodMP.World.Inventories
{
    /// The bytes of MsgType.ContainerBusy (host to client): ObjRef, str
    /// who: someone else has the container open.
    public static class ContainerBusyMessage
    {
        /// The payload, after Begin(MsgType.ContainerBusy).
        public static void Write(PacketWriter w, ObjName box, string who)
        {
            box.Write(w);
            w.WriteStr(who);
        }

        /// The container, and the name of the player who has it open.
        public static ObjName Read(PacketReader r, out string who)
        {
            var box = ObjName.Read(r);
            who = r.ReadStr();
            return box;
        }
    }
}
