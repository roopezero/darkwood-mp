namespace DarkwoodMP.World.Infections
{
    /// What MsgType.InfectionSpawned carries: the host's new patch.
    public readonly struct SpawnedPatch
    {
        /// The id the host gave it (ObjRef Spawned).
        public long Id { get; init; }

        /// Where it is.
        public Vec3 At { get; init; }

        /// The ObjRef of its parent, a location; the zero value: none.
        public ObjName Parent { get; init; }
    }
}
