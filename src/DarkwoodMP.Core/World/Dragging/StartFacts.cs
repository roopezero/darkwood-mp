namespace DarkwoodMP.World.Dragging
{
    /// What our game knows when our player has just taken hold of a thing
    /// (DragSync.StartDrag).
    public readonly struct StartFacts
    {
        /// Someone else who drags it (DragRules.OtherOwner), 0 for nobody.
        public ulong Other { get; init; }

        /// Its name, pinned now, is valid (ObjRef.Valid).
        public bool Named { get; init; }

        /// This game is the host (Session.IsHost).
        public bool Host { get; init; }
    }
}
