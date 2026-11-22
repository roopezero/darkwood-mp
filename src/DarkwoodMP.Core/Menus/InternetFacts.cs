namespace DarkwoodMP.Menus
{
    /// What a Direct host knows of its address on the internet
    /// (DirectPanel.DrawInSession).
    public readonly struct InternetFacts
    {
        /// The router forwards the port (PortMapper.Mapped).
        public bool Mapped { get; init; }

        /// The router's address on the internet, null when not known
        /// (PortMapper.ExternalIp).
        public string RouterIp { get; init; }

        /// The port the router forwards (PortMapper.ExternalPort).
        public int RouterPort { get; init; }

        /// The router is itself behind another router, so its address
        /// isn't the one others reach (PortMapper.BehindAnotherNat).
        public bool BehindAnotherNat { get; init; }

        /// The address api.ipify.org gave, null until it answered
        /// (DirectPanel._publicIp).
        public string PublicIp { get; init; }

        /// The port the game listens on (TcpBackend.Port).
        public int Port { get; init; }
    }
}
