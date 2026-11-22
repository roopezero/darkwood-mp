namespace DarkwoodMP.World.Building
{
    /// What the host knows about the construction a client's Construct
    /// names, and the option it built.
    public readonly struct BuildFacts
    {
        /// The Constructible was found by its ObjRef.
        public bool Found { get; init; }

        /// The option the client sent.
        public int Option { get; init; }

        /// How many options it has (options.Count).
        public int Options { get; init; }

        /// It may only be built when broken (onlyWhenDestroyed).
        public bool OnlyWhenDestroyed { get; init; }

        /// It is built (constructed).
        public bool Constructed { get; init; }

        /// It has an Item, and that item is destroyed.
        public bool Broken { get; init; }

        /// Its name, for the reach refusal's words.
        public string Name { get; init; }

        /// The requester and the construction. The rule sets the reach and
        /// the name.
        public ReachFacts Reach { get; init; }
    }
}
