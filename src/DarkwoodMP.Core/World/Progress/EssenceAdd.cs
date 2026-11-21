using DarkwoodMP.Net;

namespace DarkwoodMP.World.Progress
{
    /// The bytes of MsgType.EssenceAdd (client to host): i32, the essence
    /// the client's player cooked.
    public static class EssenceAdd
    {
        /// The payload, after Begin(MsgType.EssenceAdd).
        public static void Write(PacketWriter w, int amount)
        {
            w.W.Write(amount);
        }

        /// The essence the client says it cooked.
        public static int Read(PacketReader r)
        {
            return r.R.ReadInt32();
        }
    }
}
