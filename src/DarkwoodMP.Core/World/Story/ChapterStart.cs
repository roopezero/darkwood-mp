using DarkwoodMP.Net;

namespace DarkwoodMP.World.Story
{
    /// The bytes of MsgType.ChapterStart (host to clients): i32 chapter.
    public static class ChapterStart
    {
        /// The payload, after Begin(MsgType.ChapterStart).
        public static void Write(PacketWriter w, int chapter)
        {
            w.W.Write(chapter);
        }

        /// The chapter, as written.
        public static int Read(PacketReader r)
        {
            return r.R.ReadInt32();
        }
    }
}
