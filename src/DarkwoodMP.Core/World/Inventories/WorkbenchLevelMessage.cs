using DarkwoodMP.Net;

namespace DarkwoodMP.World.Inventories
{
    /// The bytes of MsgType.WorkbenchLevel (either way): i32 level the
    /// workbench was upgraded to.
    public static class WorkbenchLevelMessage
    {
        /// The payload, after Begin(MsgType.WorkbenchLevel).
        public static void Write(PacketWriter w, int level)
        {
            w.W.Write(level);
        }

        /// The level, as sent.
        public static int Read(PacketReader r)
        {
            return r.R.ReadInt32();
        }
    }
}
