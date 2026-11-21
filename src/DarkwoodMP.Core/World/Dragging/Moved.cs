namespace DarkwoodMP.World.Dragging
{
    /// What one ObjMove says: where a thing is now and how it is turned,
    /// and whether that is the end of the move (the drag ended there, or a
    /// push came to rest there).
    public readonly struct Moved
    {
        public ObjName Name { get; init; }
        public Vec3 Pos { get; init; }
        public Rotation Rot { get; init; }
        public bool Last { get; init; }
    }
}
