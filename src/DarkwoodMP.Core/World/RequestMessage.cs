using System;
using DarkwoodMP.Net;

namespace DarkwoodMP.World
{
    /// The bytes of MsgType.Request (client to host): u32 seq, then a
    /// whole message (its type byte and payload) that the host must agree
    /// to. Named for its MsgType; "Request" alone is Authority's method.
    public static class RequestMessage
    {
        /// The payload, after Begin(MsgType.Request). The message is a
        /// copy: Begin reuses the writer the message was written with.
        public static void Write(
            PacketWriter w,
            uint seq,
            ArraySegment<byte> message)
        {
            w.W.Write(seq);
            w.W.Write(message.Array, message.Offset, message.Count);
        }

        /// The request's number, and the message inside it (the rest of
        /// the payload, at most AuthorityRules.MaxRequest bytes).
        public static byte[] Read(PacketReader r, out uint seq)
        {
            seq = r.R.ReadUInt32();
            return r.R.ReadBytes(AuthorityRules.MaxRequest);
        }
    }
}
