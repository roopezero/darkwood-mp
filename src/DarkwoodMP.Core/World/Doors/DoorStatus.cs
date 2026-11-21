namespace DarkwoodMP.World.Doors
{
    /// The parts of a door's state a rollback compares: the host's, from
    /// its ObjState, or ours.
    public readonly struct DoorStatus
    {
        /// Door.opened.
        public bool Opened { get; init; }

        /// Door.barricaded.
        public bool Barricaded { get; init; }

        /// Door.destroyed.
        public bool Destroyed { get; init; }

        /// It has a Locked component that is locked.
        public bool Locked { get; init; }
    }
}
