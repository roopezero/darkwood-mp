namespace DarkwoodMP.World.Nights
{
    /// Core.AddPrefab made an object, on the host.
    public readonly struct AddedFacts
    {
        /// Both the object and its prefab exist.
        public bool Made { get; init; }

        /// The host records night objects (NightObjectRules.Records).
        public bool Records { get; init; }

        /// A night event fires (NightObjects.Firing) whose RandomEvent is
        /// a locationEvent.
        public bool LocationEvent { get; init; }

        /// The prefab is one of that event's gameEvents.
        public bool InGroup { get; init; }

        /// The copy has GameEvents.
        public bool CopyHasEvents { get; init; }

        /// A step runs (NightObjects.Steps isn't empty, its top not null).
        public bool HasStep { get; init; }

        /// The step is a gameObject step of modify type spawn.
        public bool SpawnStep { get; init; }

        /// Its targetTransform's object is the prefab.
        public bool TargetIsPrefab { get; init; }

        /// The step is tagged (made from a night event's group).
        public bool Tagged { get; init; }

        /// The made object has a Character.
        public bool Character { get; init; }

        /// That Character has an npc.
        public bool HasNpc { get; init; }
    }
}
