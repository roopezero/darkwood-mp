namespace DarkwoodMP.World.Doors
{
    /// A door and its leaf in this game, for the leaf's rules (the
    /// plugin's DoorLeaf and DoorPush).
    public readonly struct LeafDoorFacts
    {
        /// The Door is there (not null, not destroyed by Unity).
        public bool Exists { get; init; }

        /// Door.body is there.
        public bool HasBody { get; init; }

        /// The leaf's Rigidbody (Door's private bodyRB) is there. When a
        /// door is found from a collider (DoorPush.Look), it is the
        /// collider's Rigidbody. For a watched door (Unwatches, Waits), it
        /// is the Rigidbody DoorPush kept when the watch began.
        public bool HasRigidbody { get; init; }

        /// Door.opened.
        public bool Opened { get; init; }

        /// Door.destroyed.
        public bool Destroyed { get; init; }

        /// Door.barricaded.
        public bool Barricaded { get; init; }

        /// Door.body's GameObject is active in the hierarchy (a leaf out
        /// of the loaded area is switched off).
        public bool Active { get; init; }

        /// The leaf's Rigidbody.isKinematic; for a watched door, that of
        /// the Rigidbody DoorPush kept.
        public bool Kinematic { get; init; }

        /// This game is turning the leaf to another game's angle.
        public bool Gliding { get; init; }

        /// This game watches the leaf for pushes already.
        public bool Watched { get; init; }

        /// From our player to the leaf (Door.body.position), on the
        /// ground.
        public float Distance { get; init; }
    }
}
