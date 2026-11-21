namespace DarkwoodMP.Net.Steam
{
    /// How SteamTransport sends: which messages go in pieces, which are
    /// refused, when one waits in the queue for room, and when a peer is
    /// closed once its messages are gone (Send, SendOne, Enqueue,
    /// FlushPending, CloseSent).
    public static class SteamSendRules
    {
        /// The longest message Steam takes in one
        /// (k_cbMaxSteamNetworkingSocketsMessageSizeSend), in bytes.
        public const int MaxMessage = 512 << 10;

        /// The data in each SteamPiece of a longer reliable message.
        public const int PieceSize = 256 << 10;

        /// Past this many bytes waiting for one peer, it isn't taking
        /// anything: new messages to it are refused.
        public const long MaxWaiting = 64L << 20;

        /// How long after a peer's queue empties its session is closed, in
        /// unscaled seconds, so Steam can deliver its own queue first.
        public const float CloseDelay = 1f;

        /// Whether a message is too long for Steam in one: a reliable one
        /// then goes in pieces, an unreliable one is refused (TooLong). The
        /// check runs before the queue.
        public static bool TooLongForOne(int length)
        {
            return length > MaxMessage;
        }

        /// The refusal of an unreliable message too long for Steam. type is
        /// the name of its MsgType (its first byte).
        public static Verdict TooLong(int length, string type)
        {
            string kib = (length / 1024).ToString();
            return Verdict.Refuse(
                Refusal.TooLongForSteam,
                Refusals.Detail(kib, type));
        }

        /// Whether a reliable message waits behind the ones already queued
        /// for that peer, without trying Steam, so the order is kept.
        public static bool WaitsBehind(int queued)
        {
            return queued > 0;
        }

        /// What to do after Steam's answer to a reliable message.
        public static SendStep AfterReliable(SendResult result)
        {
            if (result == SendResult.Full)
            {
                return SendStep.Queue;
            }

            if (result != SendResult.Sent)
            {
                return SendStep.Failed;
            }

            return SendStep.Sent;
        }

        /// Whether an unreliable message went. A failure is silent and is
        /// never queued.
        public static bool AfterUnreliable(SendResult result)
        {
            return result == SendResult.Sent;
        }

        /// Whether a message of this length may join a peer's queue that
        /// holds waiting bytes. The peer keeps what's queued either way.
        public static Verdict MayQueue(long waiting, int length)
        {
            if (waiting + length > MaxWaiting)
            {
                string kib = (waiting / 1024).ToString();
                return Verdict.Refuse(Refusal.QueueFull, kib);
            }

            return Verdict.Allow;
        }

        /// What to do after Steam's answer to a queued message.
        public static FlushStep AfterFlush(SendResult result)
        {
            if (result == SendResult.Full)
            {
                return FlushStep.Stop;
            }

            if (result != SendResult.Sent)
            {
                return FlushStep.Drop;
            }

            return FlushStep.Next;
        }

        /// What to do about a peer waiting to be closed: wait while its
        /// queue holds anything, then set the time, then close at it.
        public static CloseStep Close(in CloseFacts peer)
        {
            if (peer.Waiting)
            {
                return CloseStep.Wait;
            }

            if (!peer.Armed)
            {
                return CloseStep.Arm;
            }

            if (peer.Now.Value >= peer.At.Value)
            {
                return CloseStep.Close;
            }

            return CloseStep.Wait;
        }

        /// When to close a peer whose queue emptied at now.
        public static Seconds CloseAt(Seconds now) =>
            new Seconds(now.Value + CloseDelay);
    }
}
