namespace DarkwoodMP.World.Doors
{
    /// What a door patch looks at, after the game's call, to decide
    /// whether to send it.
    public readonly struct DoorSendFacts
    {
        /// This was the outermost patched world call
        /// (WorldActions.Outermost(), which the shell calls first).
        public bool Outermost { get; init; }

        /// The world is shared (WorldActions.Sharing).
        public bool Sharing { get; init; }

        /// The door before the call (the patch's __state).
        public DoorState Before { get; init; }

        /// The door after it.
        public DoorState After { get; init; }
    }
}
