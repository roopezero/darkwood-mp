using DarkwoodMP.Net;

namespace DarkwoodMP.World.Progress
{
    /// The bytes of MsgType.ProgressState (host to everyone): i32 the
    /// shared essence, u8 the shared level (clamped to 0 to 255).
    public static class ProgressState
    {
        /// The payload, after Begin(MsgType.ProgressState).
        public static void Write(PacketWriter w, ProgressNumbers shared)
        {
            w.W.Write(shared.Exp);
            w.W.Write(LevelTaken.Byte(shared.Level));
        }

        /// The host's shared essence and level.
        public static ProgressNumbers Read(PacketReader r)
        {
            int exp = r.R.ReadInt32();
            int level = r.R.ReadByte();
            return new ProgressNumbers { Exp = exp, Level = level };
        }
    }
}
