namespace DarkwoodMP.World
{
    /// What ObjRef.Resolve looks at to choose where to find a name.
    public readonly struct LookupFacts
    {
        /// The kind byte as it came; an unknown one names nothing.
        public ObjKind Kind { get; init; }

        public long Id { get; init; }

        /// A live object made while playing together has this id.
        public bool SpawnedAlive { get; init; }

        /// A live object is tied to this id the host gave out.
        public bool SharedAlive { get; init; }

        /// Client: the host announced this id with a name and position.
        public bool Announced { get; init; }

        /// A live object is pinned to this Placed key.
        public bool PinnedAlive { get; init; }

        /// SharedIdRules.ResolveLimit.
        public int ResolveLimit { get; init; }
    }
}
