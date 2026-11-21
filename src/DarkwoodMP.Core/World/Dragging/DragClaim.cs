using DarkwoodMP.Net;

namespace DarkwoodMP.World.Dragging
{
    /// The bytes of MsgType.DragClaim (client to host, inside a Request):
    /// ObjRef, the thing our player took hold of.
    public static class DragClaim
    {
        /// The payload, after Begin(MsgType.DragClaim).
        public static void Write(PacketWriter w, ObjName thing)
        {
            thing.Write(w);
        }

        /// The thing claimed.
        public static ObjName Read(PacketReader r)
        {
            return ObjName.Read(r);
        }
    }
}
