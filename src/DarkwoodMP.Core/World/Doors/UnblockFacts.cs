namespace DarkwoodMP.World.Doors
{
    /// What the story's unblock of a door looks at to decide whether to
    /// send it (the plugin's DoorUnblockPatch.Unblock).
    public readonly struct UnblockFacts
    {
        /// Door.blocked before Door.unblock.
        public bool WasBlocked { get; init; }

        /// Door.blocked after it.
        public bool Blocked { get; init; }

        /// How deep in patched world calls this is (WorldActions.Depth):
        /// it's sent only outside all of them.
        public int Depth { get; init; }

        /// The world is shared (WorldActions.Sharing).
        public bool Sharing { get; init; }
    }
}
