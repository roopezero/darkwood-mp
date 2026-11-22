namespace DarkwoodMP.World.Hideout
{
    /// The host: whether a player's old oven goes out as they light one.
    public readonly struct PutOutFacts
    {
        /// The old oven exists.
        public bool HasOld { get; init; }

        /// It is the oven being lit.
        public bool OldIsLit { get; init; }

        /// It is the host player's own (Player.experienceMachine).
        public bool OldIsHosts { get; init; }

        /// It is another joiner's own: any in the host's table of ovens
        /// but the one who lit (none for the host's own lighting).
        public bool OldIsOthers { get; init; }
    }
}
