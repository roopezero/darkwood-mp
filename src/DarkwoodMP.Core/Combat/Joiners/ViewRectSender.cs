namespace DarkwoodMP.Combat.Joiners
{
    /// A client's memory of the view rect it sent, and when it checks
    /// next.
    public sealed class ViewRectSender
    {
        /// The rect last sent; (0, 0) before any.
        public ViewRect Sent { get; private set; }

        /// When the client checks its rect next. Never reset.
        public Seconds NextCheck { get; private set; }

        /// Whether the client measures its rect now: it is a client in the
        /// host's world with a Controller, and the check is due. A due
        /// check sets the next one ViewRectRules.CheckEvery later.
        public bool Due(in RectSendFacts client)
        {
            if (!client.Client || !client.InWorld || !client.Controller)
            {
                return false;
            }

            if (client.Now < NextCheck)
            {
                return false;
            }

            NextCheck = new Seconds(
                client.Now.Value + ViewRectRules.CheckEvery);
            return true;
        }

        /// Whether the client sends the rect it measured: one it may send
        /// that differs from the last. It counts as sent from here, before
        /// it goes out, so one the host drops isn't sent again until it
        /// changes. Connected isn't checked.
        public bool Sends(ViewRect rect)
        {
            if (!ViewRectRules.Sendable(rect) || rect.Equals(Sent))
            {
                return false;
            }

            Sent = rect;
            return true;
        }

        /// The session ended: the last rect is forgotten, the next check
        /// time isn't.
        public void EndSession()
        {
            Sent = default;
        }
    }
}
