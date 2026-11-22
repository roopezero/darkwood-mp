namespace DarkwoodMP.World.Nights
{
    /// One of the save's dynamic objects (SaveManager.dynamicObjects).
    public readonly struct SavedObject
    {
        /// Its Transform's name.
        public string Name { get; init; }

        /// Its ObjRef is of kind Spawned (already tied to an id).
        public bool Spawned { get; init; }

        /// Its position.
        public Vec3 Pos { get; init; }
    }
}
