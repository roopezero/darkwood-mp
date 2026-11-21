namespace DarkwoodMP.World.Hideout
{
    /// A client, when the host says an oven was lit (OvenLit).
    public readonly struct HostOvenFacts
    {
        /// The lit oven resolved here.
        public bool Found { get; init; }

        /// It is on here already.
        public bool On { get; init; }

        /// The oven to put out resolved here.
        public bool HasOther { get; init; }

        /// It is the lit one.
        public bool OtherIsLit { get; init; }

        /// Player.Instance exists.
        public bool HasPlayer { get; init; }

        /// It is our player's own oven.
        public bool OtherIsOurs { get; init; }
    }
}
