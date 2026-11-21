namespace DarkwoodMP.World.Dragging
{
    /// What our game does with an ObjMove: the owner heard now (Refresh)
    /// before the action.
    public readonly struct ApplyStep
    {
        public ApplyAction Action { get; init; }

        /// Client: the owner it knows for the thing was heard now.
        public bool Refresh { get; init; }
    }
}
