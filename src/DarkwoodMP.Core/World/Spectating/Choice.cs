namespace DarkwoodMP.World.Spectating
{
    /// The player's input while watching.
    public enum Choice : byte
    {
        /// No click this frame.
        None = 0,

        /// A left click or Space: the next player.
        Next,

        /// A right click (with no left click or Space): the one before.
        Previous,
    }
}
