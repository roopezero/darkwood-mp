using DarkwoodMP.Net;

namespace DarkwoodMP.World.Dreaming
{
    /// The bytes of MsgType.DreamEnd (both ways): str the outcome, str the
    /// shared dream that ended ("" for none).
    public static class DreamEnd
    {
        /// The payload, after Begin(MsgType.DreamEnd). A null outcome or
        /// dream is written as "".
        public static void Write(PacketWriter w, in DreamEnded end)
        {
            w.WriteStr(OrEmpty(end.Outcome));
            w.WriteStr(OrEmpty(end.Dream));
        }

        /// The outcome and the dream.
        public static DreamEnded Read(PacketReader r)
        {
            string outcome = r.ReadStr();
            string dream = r.ReadStr();
            return new DreamEnded { Outcome = outcome, Dream = dream };
        }

        static string OrEmpty(string s)
        {
            if (s == null)
            {
                return "";
            }

            return s;
        }
    }
}
