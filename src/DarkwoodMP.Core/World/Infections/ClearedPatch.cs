namespace DarkwoodMP.World.Infections
{
    /// One patch of an InfectionCleared: the patch, and where the sender
    /// saw it, to find it by when its name isn't known here.
    public readonly struct ClearedPatch
    {
        /// The patch's ObjRef.
        public ObjName Patch { get; init; }

        /// Where the sender saw it, on the ground plane.
        public Vec2 At { get; init; }
    }
}
