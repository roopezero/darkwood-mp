namespace DarkwoodMP.World.Inventories
{
    /// What SlotShare.SortPressed looks at when the local player presses
    /// Sort & Stack.
    public readonly struct SortPressFacts
    {
        /// Our player holds an item on the cursor
        /// (Controller.pickedUpItem isn't null).
        public bool Holding { get; init; }

        /// This game is a client.
        public bool IsClient { get; init; }

        /// It's in a world (WorldShare.InWorld).
        public bool InWorld { get; init; }

        /// The storage has a name in the host's world (a valid ObjRef).
        public bool Named { get; init; }

        /// We still wait for the host's contents of it.
        public bool Waiting { get; init; }
    }
}
