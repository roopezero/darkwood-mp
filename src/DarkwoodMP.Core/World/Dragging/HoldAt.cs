namespace DarkwoodMP.World.Dragging
{
    /// Where our copy of a thing we let go of is held still, as a last
    /// move.
    public enum HoldAt : byte
    {
        /// Not held: it is shown following another game's move already.
        None = 0,

        /// At the host's last word on it.
        HostWord,

        /// Where it is known to be as a loose thing.
        Known,

        /// Where it is.
        Here,
    }
}
