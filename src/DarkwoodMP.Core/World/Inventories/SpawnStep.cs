namespace DarkwoodMP.World.Inventories
{
    /// What Containers.OnSpawn does with something another game made.
    public enum SpawnStep : byte
    {
        /// Nothing: not a shared kind, or it's here already.
        Ignore = 0,

        /// A client placed it outside a request: a warning, and nothing.
        Unasked,

        /// Refused (the verdict says why): nothing is made.
        Refuse,

        /// Make it here.
        Make,
    }
}
