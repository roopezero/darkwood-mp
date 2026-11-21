namespace DarkwoodMP.World.Inventories
{
    /// What Containers.MadeFrom looks at when the game made something with
    /// the Object overload of Core.AddPrefab.
    public readonly struct MadeFromFacts
    {
        /// A thrown item is going off on the host and makes what it leaves
        /// behind (Containers.ThrownSpawning is set).
        public bool Thrown { get; init; }

        /// Our player is placing something (Containers.Placing).
        public bool Placing { get; init; }

        /// The game gave back an object.
        public bool Exists { get; init; }

        /// Coop.Guards.Replaying.
        public bool Replaying { get; init; }

        /// Coop.Guards.Sharing.
        public bool Sharing { get; init; }

        /// The prefab is one a player can place, so it has a path
        /// (Containers.PlaceablePath isn't null).
        public bool Placeable { get; init; }
    }
}
