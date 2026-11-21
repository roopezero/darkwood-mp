namespace DarkwoodMP.World.Dragging
{
    /// What happens first to an ObjMove that comes in.
    public enum Arrival : byte
    {
        /// Not in the world, or a client's move from anyone but the host.
        Drop = 0,

        /// Host: DragRules.TakeMove decides, then a taken move is relayed
        /// and applied.
        Check,

        /// Client: applied (ObjMoveRules.Apply).
        Apply,
    }
}
