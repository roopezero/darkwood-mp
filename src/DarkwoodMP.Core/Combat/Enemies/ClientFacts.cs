namespace DarkwoodMP.Combat.Enemies
{
    /// What decides that a client is in the host's world (the plugin's
    /// NetEnemies.ClientMode).
    public readonly struct ClientFacts
    {
        /// ModHost.Session isn't null.
        public bool HasSession { get; init; }

        /// Session.IsClient.
        public bool Client { get; init; }

        /// Session.Connected.
        public bool Connected { get; init; }

        /// SaveRedirect.Active: the host's world is loaded.
        public bool Redirected { get; init; }
    }
}
