using DarkwoodMP.Net;

namespace DarkwoodMP.World.Progress
{
    /// The bytes of MsgType.LevelTaken (client to host): u8, the client's
    /// own level (Player.currentLevel, clamped to 0 to 255).
    public static class LevelTaken
    {
        /// The payload, after Begin(MsgType.LevelTaken).
        public static void Write(PacketWriter w, int level)
        {
            w.W.Write(Byte(level));
        }

        /// The level the client says it has.
        public static int Read(PacketReader r)
        {
            return r.R.ReadByte();
        }

        /// A level as one byte: below 0 is 0, above 255 is 255.
        public static byte Byte(int level)
        {
            if (level < 0)
            {
                return 0;
            }

            if (level > 255)
            {
                return 255;
            }

            return (byte)level;
        }
    }
}
