namespace DarkwoodMP.World.Dragging
{
    /// What the host does with a claim.
    public enum ClaimAnswer : byte
    {
        /// Not the host, or not inside a request: nothing at all.
        None = 0,

        /// The request is refused (ClaimOutcome.Verdict).
        Refused,

        /// The claimer drags it now: their entry in the owner table, heard
        /// now, and DragOwner(claimer, let go) to everyone.
        Agreed,
    }
}
