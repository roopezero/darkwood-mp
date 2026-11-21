namespace DarkwoodMP.World.Dreaming
{
    /// What our game does as its own shared dream ends (DreamSync.Ending).
    public enum EndSend
    {
        /// Nothing: no shared dream ending here, or a replayed end.
        None,

        /// Marked ended, with nothing sent (neither host nor client).
        Mark,

        /// Marked ended and sent to everyone (the host).
        Broadcast,

        /// Marked ended and sent to the host (a client).
        ToHost,
    }
}
