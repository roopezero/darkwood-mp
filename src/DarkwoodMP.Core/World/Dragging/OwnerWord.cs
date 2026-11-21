namespace DarkwoodMP.World.Dragging
{
    /// What one DragOwner says: who drags the thing now (0: nobody), and
    /// whether a player dragging it who isn't the owner lets go.
    public readonly struct OwnerWord
    {
        public ObjName Name { get; init; }
        public ulong Owner { get; init; }
        public bool LetGo { get; init; }
    }
}
