namespace DarkwoodMP.Menus
{
    /// One server in the browser's list, as its sort reads it
    /// (DirectPanel.Sorted).
    public readonly struct ServerRow
    {
        /// The address the list holds it by.
        public string Address { get; init; }

        /// It answered the query (the entry's Info isn't null).
        public bool Known { get; init; }

        /// It says it has a password (Info.Password).
        public bool Password { get; init; }

        /// Its name (Info.Name), null when it gave none.
        public string Name { get; init; }

        /// How many play on it (Info.Players).
        public int Players { get; init; }

        /// Its world (Info.Fields["world"]), null when it gave none.
        public string World { get; init; }

        /// How long its answer took, in ms (the entry's PingMs).
        public int Ping { get; init; }
    }
}
