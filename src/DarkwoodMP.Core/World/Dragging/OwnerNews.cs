namespace DarkwoodMP.World.Dragging
{
    /// What a DragOwner does: first to the owner table, then to our drag.
    public readonly struct OwnerNews
    {
        public OwnerEntry Entry { get; init; }

        public ClaimReply Drag { get; init; }
    }
}
