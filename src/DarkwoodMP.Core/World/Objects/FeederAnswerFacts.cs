namespace DarkwoodMP.World.Objects
{
    /// What a client knows when the host answers its player's use of a
    /// feeder (the callback of the plugin's ObjectActions.RequestFeeder).
    public readonly struct FeederAnswerFacts
    {
        /// The feeder and Player.Instance are both still there.
        public bool Here { get; init; }

        /// The host said yes.
        public bool Ok { get; init; }
    }
}
