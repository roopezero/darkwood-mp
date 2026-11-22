namespace DarkwoodMP.World.Dragging
{
    /// What our game does as our player takes hold of a thing.
    public enum StartAction : byte
    {
        /// Someone else drags it: let go at once (CancelLocal) with
        /// "NAME is moving it".
        Refused = 0,

        /// No valid name: nothing is ever sent for this drag.
        Unnamed,

        /// Host: ours, heard now, DragOwner(host, let go) to everyone.
        Own,

        /// Client: the host is asked (a Request holding DragClaim).
        Claim,
    }
}
