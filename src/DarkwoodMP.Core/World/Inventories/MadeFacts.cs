namespace DarkwoodMP.World.Inventories
{
    /// What Containers.Made looks at when the game made something with
    /// Core.AddPrefab.
    public readonly struct MadeFacts
    {
        /// The game gave back an object.
        public bool Exists { get; init; }

        /// It was made while applying another game's change
        /// (Coop.Guards.Replaying).
        public bool Replaying { get; init; }

        /// The world is shared now (Coop.Guards.Sharing).
        public bool Sharing { get; init; }

        /// The prefab's path under Resources/Prefabs.
        public string Prefab { get; init; }

        /// Our player is placing something (Containers.Placing).
        public bool Placing { get; init; }

        /// It's in a dream of our own (WorldActions.OursAlone).
        public bool OursAlone { get; init; }
    }
}
