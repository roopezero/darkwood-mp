namespace DarkwoodMP.Menus
{
    /// The Direct section of the Multiplayer window: which part shows, and
    /// what it offers (DirectPanel.Draw and DrawInSession).
    public static class DirectRules
    {
        /// What TcpBackend.Status says when nothing is going on: the idle
        /// section shows no status line then.
        public const string NotConnected = "Not connected";

        /// A Steam session wins over the TCP backend; then any TCP
        /// activity, a session or not; else the idle section.
        public static DirectView View(in DirectFacts direct)
        {
            if (direct.Active && !direct.OnTcp)
            {
                if (direct.Host)
                {
                    return DirectView.SteamHost;
                }

                return DirectView.SteamClient;
            }

            if (direct.TcpBusy)
            {
                return DirectView.DirectGame;
            }

            return DirectView.Idle;
        }

        /// The idle section's first line: the host's last refusal
        /// (Session.LastRefusal), else the TCP status unless it's "Not
        /// connected". Null for no line.
        public static string IdleLine(string refusal, string status)
        {
            if (refusal != null)
            {
                return "Refused: " + refusal;
            }

            if (status != NotConnected)
            {
                return status;
            }

            return null;
        }

        /// A Direct host's internet row: the router's address when it
        /// forwards the port and isn't behind another router, else the
        /// looked-up address on the game's port, else none yet.
        public static InternetRow Internet(in InternetFacts net)
        {
            if (net.Mapped && net.RouterIp != null && !net.BehindAnotherNat)
            {
                return new InternetRow
                {
                    Address = net.RouterIp + ":" + net.RouterPort,
                    Note = "internet",
                };
            }

            if (net.PublicIp == null)
            {
                return default;
            }

            var note = "internet: forward the port";
            if (net.Mapped)
            {
                note = "internet";
            }

            return new InternetRow
            {
                Address = net.PublicIp + ":" + net.Port,
                Note = note,
            };
        }

        /// A joiner in a Direct game that is connected may reload the
        /// host's world.
        public static bool MayReload(bool host, bool connected) =>
            !host && connected;
    }
}
