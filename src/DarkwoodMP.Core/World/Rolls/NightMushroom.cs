namespace DarkwoodMP.World.Rolls
{
    /// A night mushroom the host made (RandomObjectsMessage.Night).
    public readonly struct NightMushroom
    {
        /// Its spawned id (ObjRef).
        public long Id { get; init; }

        /// Its prefab's name.
        public string Prefab { get; init; }

        /// Where it is, in world space.
        public Vec3 Pos { get; init; }
    }
}
