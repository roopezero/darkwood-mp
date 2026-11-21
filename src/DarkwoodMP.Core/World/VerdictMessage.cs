using DarkwoodMP.Net;

namespace DarkwoodMP.World
{
    /// The bytes of MsgType.Verdict (host to client): u32 seq, u8 ok, str
    /// reason (why it was refused, "" if not), str result (what the host
    /// decided, "" if nothing). Named for its MsgType; "Verdict" alone is
    /// a rule's answer.
    public static class VerdictMessage
    {
        /// The payload, after Begin(MsgType.Verdict): the answer to
        /// request seq, with the refusal's words from Refusals.Text.
        public static void Write(
            PacketWriter w,
            uint seq,
            in RequestAnswer answer)
        {
            w.W.Write(seq);
            byte ok = 1;
            if (answer.Verdict.Refused)
            {
                ok = 0;
            }

            w.W.Write(ok);
            w.WriteStr(Refusals.Text(answer.Verdict));
            w.WriteStr(answer.Result);
        }

        /// The host's answer to one of our requests.
        public static VerdictReply Read(PacketReader r)
        {
            return new VerdictReply
            {
                Seq = r.R.ReadUInt32(),
                Ok = r.R.ReadByte() != 0,
                Reason = r.ReadStr(),
                Result = r.ReadStr(),
            };
        }
    }
}
