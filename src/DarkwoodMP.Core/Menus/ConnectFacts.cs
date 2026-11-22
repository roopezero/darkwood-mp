namespace DarkwoodMP.Menus
{
    /// What Connect looks at when the player joins a server
    /// (DirectPanel.Connect).
    public readonly struct ConnectFacts
    {
        /// The server's address, null for none.
        public string Address { get; init; }

        /// The server answered the browser's query (its entry's Info isn't
        /// null).
        public bool Known { get; init; }

        /// It answered with another version (Info.Incompatibility isn't
        /// null).
        public bool Incompatible { get; init; }

        /// It says it needs a password (Info.Password).
        public bool NeedsPassword { get; init; }

        /// A password came with the call, "" included (the password
        /// window, the address window and JoinForTest give one; the
        /// browser's Connect button and a double click don't).
        public bool PasswordGiven { get; init; }

        /// A session is active (Session.Active).
        public bool InGame { get; init; }

        /// The player hosts it (Session.IsHost).
        public bool Host { get; init; }
    }
}
