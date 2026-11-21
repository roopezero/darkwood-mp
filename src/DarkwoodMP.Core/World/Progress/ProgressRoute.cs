namespace DarkwoodMP.World.Progress
{
    /// What our game does with a change to its player's progression.
    public enum ProgressRoute
    {
        /// Nothing.
        None,

        /// A client's way: Cooked predicts and asks the host; a level or a
        /// reset sends the level and applies; Update applies.
        Client,

        /// The host's (or a solo game's) way: Progression.HostSync.
        Host,
    }
}
