namespace DarkwoodMP.World.Inventories
{
    /// What Containers.OnSpawn looks at in something another game made
    /// (ObjSpawn), once ContainerRules.Accepts took the message.
    public readonly struct SpawnFacts
    {
        /// This game is the host.
        public bool IsHost { get; init; }

        /// This game is a client.
        public bool IsClient { get; init; }

        /// The prefab's path under Resources/Prefabs, as sent.
        public string Prefab { get; init; }

        /// A player placed it (the message's flag).
        public bool Placed { get; init; }

        /// Its id names something here already (ObjRef.Spawned).
        public bool Known { get; init; }

        /// The host is handling a request (Authority.Handling).
        public bool Handling { get; init; }

        /// A player can place that prefab (Containers.IsPlaceable).
        public bool Placeable { get; init; }

        /// ReachRules.InReach for where it is, with the reach
        /// ReachRules.Reach and the prefab as what.
        public Verdict Reach { get; init; }

        /// The host has the client's character this session
        /// (Characters.Carries gave a number).
        public bool CharacterKnown { get; init; }

        /// How many items that place it the character carries, when
        /// CharacterKnown.
        public int Carried { get; init; }

        /// The prefab is in this game (Resources.Load isn't null).
        public bool PrefabExists { get; init; }
    }
}
