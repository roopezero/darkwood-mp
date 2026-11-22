using DarkwoodMP.Net;

namespace DarkwoodMP.World.Story
{
    /// The bytes of MsgType.StoryRequest (client to host, inside a
    /// Request): ObjRef of the GameEvents, u8 step index, u8 kind
    /// (StoryKind), i32 chapter (0 unless a chapter).
    public static class StoryRequest
    {
        /// The payload, after Begin(MsgType.StoryRequest).
        public static void Write(PacketWriter w, in StoryAsk ask)
        {
            ask.Event.Write(w);
            w.W.Write(ask.Index);
            w.W.Write((byte)ask.Kind);
            w.W.Write(ask.Chapter);
        }

        /// The request, as written: an unknown kind byte is kept.
        public static StoryAsk Read(PacketReader r)
        {
            var name = ObjName.Read(r);
            byte index = r.R.ReadByte();
            var kind = (StoryKind)r.R.ReadByte();
            int chapter = r.R.ReadInt32();
            return new StoryAsk
            {
                Event = name,
                Index = index,
                Kind = kind,
                Chapter = chapter,
            };
        }
    }
}
