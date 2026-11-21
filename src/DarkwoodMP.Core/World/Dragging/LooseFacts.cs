namespace DarkwoodMP.World.Dragging
{
    /// A body that may be watched for pushes (DragSync.LooseItem).
    public readonly struct LooseFacts
    {
        /// There is a body (the Rigidbody isn't null).
        public bool Body { get; init; }

        /// Its constraints are RigidbodyConstraints.FreezeAll.
        public bool Frozen { get; init; }

        /// It has an Item.
        public bool Item { get; init; }

        /// It has a CharBase (a character).
        public bool Character { get; init; }

        /// A Door is among its parents (GetComponentInParent).
        public bool InDoor { get; init; }

        /// A Window is among its parents.
        public bool InWindow { get; init; }
    }
}
