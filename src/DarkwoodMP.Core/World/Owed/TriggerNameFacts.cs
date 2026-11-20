namespace DarkwoodMP.World.Owed
{
    /// What names an event trigger the same way in every game and after
    /// the world is loaded again (OwedRules.IdOf).
    public readonly struct TriggerNameFacts
    {
        /// Its EventTriggers object has a SaveableObject.
        public bool Saved { get; init; }

        /// That SaveableObject's uniqueId, when Saved.
        public int SaveId { get; init; }

        /// The trigger's index in its object's eventTriggers (IndexOf: -1
        /// when it isn't in the list).
        public int Index { get; init; }

        /// The name of the object's scene (gameObject.scene.name).
        public string Scene { get; init; }

        /// The object's name.
        public string Name { get; init; }

        /// The object's position (transform.position) along x.
        public float X { get; init; }

        /// The object's position along z.
        public float Z { get; init; }
    }
}
