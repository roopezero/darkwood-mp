namespace DarkwoodMP.World.Hideout
{
    /// Who does a porter transport.
    public enum TransportStep : byte
    {
        /// Not together: the game's own transport runs.
        RunGame = 0,

        /// A client's own doing: ask the host (PorterTransport).
        Request,

        /// Nothing here: the host's result comes, or the joiner's request.
        Nothing,

        /// The host's own: check PorterRules.Why against where our player
        /// is, then deliver, or log why not.
        Deliver,
    }
}
