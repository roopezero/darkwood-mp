using DarkwoodMP.Net;

namespace DarkwoodMP.World.Story
{
    /// The bytes of MsgType.GameEnd (host to clients): u8 kind (EndKind),
    /// u8 phase (0: the epilogue begins, 1: the session ends).
    public static class GameEnd
    {
        /// The payload, after Begin(MsgType.GameEnd).
        public static void Write(PacketWriter w, in EndWord word)
        {
            w.W.Write((byte)word.Kind);
            w.W.Write(word.Phase);
        }

        /// The ending, as written: an unknown kind byte is kept.
        public static EndWord Read(PacketReader r)
        {
            var kind = (EndKind)r.R.ReadByte();
            byte phase = r.R.ReadByte();
            return new EndWord
            {
                Kind = kind,
                Phase = phase,
            };
        }
    }
}
