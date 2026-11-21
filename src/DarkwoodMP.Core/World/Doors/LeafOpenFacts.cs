namespace DarkwoodMP.World.Doors
{
    /// What DoorPush.Opened looks at when this game opened a door.
    public readonly struct LeafOpenFacts
    {
        /// The world is shared (WorldActions.Sharing).
        public bool Sharing { get; init; }

        /// The open came from another game (WorldActions.Replaying).
        public bool Replaying { get; init; }

        /// The door and its leaf.
        public LeafDoorFacts Door { get; init; }
    }
}
